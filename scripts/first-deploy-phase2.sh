#!/usr/bin/env bash
# First-deploy Phase 2: Config & secrets (T-5.2.1 – T-5.2.4)
# Creates secrets and SSM parameters. For dev, API keys and OpenAI key are stored in
# SSM Parameter Store (SecureString) to avoid Secrets Manager per-secret cost; staging/prod use Secrets Manager.
#
# Prerequisites:
#   - AWS credentials configured
#   - Phase 1 complete (Terraform apply done; DynamoDB, SQS, S3 exist)
#   - jq installed
#
# Optional env vars (otherwise placeholders are used):
#   - API_KEYS_JSON: JSON array of API keys, e.g. '["key1","key2"]'
#   - OPENAI_API_KEY: OpenAI API key for the Provider Worker
#
# If API_KEYS_JSON is not set and Bitwarden CLI (bw) is installed and unlocked,
# the script will generate a new API key and create a secure note in Bitwarden
# (e.g. "Prompt Gateway dev API keys") so you have a copy, then use it for AWS.
#
# Usage: ./scripts/first-deploy-phase2.sh [--skip-secrets] [--skip-ssm]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV="${ENV:-dev}"
REGION="${AWS_REGION:-us-east-1}"
SKIP_SECRETS=false
SKIP_SSM=false

for arg in "$@"; do
  case $arg in
    --skip-secrets) SKIP_SECRETS=true ;;
    --skip-ssm)     SKIP_SSM=true ;;
  esac
done

echo "=== First-deploy Phase 2: Config & secrets (T-5.2) ==="
echo "Environment: $ENV  Region: $REGION"
echo ""

# Resolve resource names (must match Phase 1 / Terraform)
ACCOUNT=$(aws sts get-caller-identity --query Account --output text 2>/dev/null)
TABLE_NAME="prompt-gateway-${ENV}"
DISPATCH_QUEUE="prompt-gateway-${ENV}-dispatch"
RESULT_QUEUE="prompt-gateway-${ENV}-result"
PROMPTS_BUCKET="prompt-gateway-${ENV}-prompts-${ACCOUNT}"
RESULTS_BUCKET="prompt-gateway-${ENV}-results-${ACCOUNT}"
SSM_PREFIX="/prompt-gateway/${ENV}"

# --- T-5.2.1: Create secret prompt-gateway/{env}/api-keys (JSON array) ---
if [ "$SKIP_SECRETS" = false ]; then
  echo "T-5.2.1: Create secret ${SSM_PREFIX}/api-keys (JSON array)..."
  if [ -z "${API_KEYS_JSON:-}" ]; then
    if command -v bw >/dev/null 2>&1; then
      BW_STATUS=$(bw status 2>/dev/null | jq -r '.status // empty')
      if [ "$BW_STATUS" = "unlocked" ]; then
        BW_ITEM_NAME="Prompt Gateway ${ENV} API keys"
        # Reuse existing note if present (bw get notes requires item ID, not name)
        BW_ITEM_ID=$(bw list items --search "$BW_ITEM_NAME" 2>/dev/null | jq -r --arg name "$BW_ITEM_NAME" '.[] | select(.name == $name) | .id' | head -1)
        EXISTING_NOTES=""
        [ -n "$BW_ITEM_ID" ] && EXISTING_NOTES=$(bw get notes "$BW_ITEM_ID" 2>/dev/null || true)
        if [ -n "$EXISTING_NOTES" ] && echo "$EXISTING_NOTES" | jq -e 'type == "array" and length > 0' >/dev/null 2>&1; then
          API_KEYS_JSON=$(echo "$EXISTING_NOTES" | jq -c '.')
          echo "  OK: Using existing API key(s) from Bitwarden note \"$BW_ITEM_NAME\"."
        else
          echo "  Generating new API key and storing in Bitwarden..."
          NEW_KEY=$(openssl rand -base64 32 | tr -d '\n')
          API_KEYS_JSON=$(jq -n --arg k "$NEW_KEY" '[$k]' -c)
          # Build secure-note JSON (type 2) with jq, then bw encode | bw create item
          BW_JSON=$(jq -n -c --arg name "$BW_ITEM_NAME" --arg notes "$API_KEYS_JSON" '{type:2,name:$name,notes:$notes,passwordHistory:[],revisionDate:null,creationDate:null,deletedDate:null,organizationId:null,collectionIds:null,folderId:null,favorite:false,fields:[],login:null,secureNote:{type:0},card:null,identity:null,reprompt:0}')
          BW_TMP=$(mktemp)
          ( echo "$BW_JSON" | bw encode 2>/dev/null | bw create item > "$BW_TMP" 2>&1; echo $? >> "$BW_TMP.rc" ) &
          BW_PID=$!
          for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do kill -0 $BW_PID 2>/dev/null || break; sleep 1; done
          if kill -0 $BW_PID 2>/dev/null; then
            kill $BW_PID 2>/dev/null; wait $BW_PID 2>/dev/null
            BW_OUT="Bitwarden create timed out after 20s. Run 'bw unlock' and try again, or set API_KEYS_JSON."
            BW_RC=1
          else
            wait $BW_PID
            BW_RC=$(tail -1 "$BW_TMP.rc" 2>/dev/null)
            BW_RC=${BW_RC:-1}
            BW_OUT=$(cat "$BW_TMP" 2>/dev/null)
          fi
          rm -f "$BW_TMP" "$BW_TMP.rc"
          if [ "${BW_RC:-1}" -eq 0 ] && echo "$BW_OUT" | jq -e '.id' >/dev/null 2>&1; then
            echo "  OK: Created secure note \"$BW_ITEM_NAME\" in Bitwarden with the new key."
          else
            echo "  WARN: Bitwarden create failed; using key for AWS only (not stored in bw)."
            [ -n "$BW_OUT" ] && echo "  bw error: $BW_OUT"
          fi
        fi
      else
        API_KEYS_JSON='["change-me-dev-key"]'
        echo "  NOTE: Bitwarden not unlocked (status=$BW_STATUS). Using placeholder. Run 'bw unlock' and re-run to generate and store keys in bw."
      fi
    else
      API_KEYS_JSON='["change-me-dev-key"]'
      echo "  NOTE: API_KEYS_JSON not set and Bitwarden CLI (bw) not found. Using placeholder."
    fi
  fi
  API_KEYS_JSON="${API_KEYS_JSON:-[\"change-me-dev-key\"]}"
  OPENAI_KEY="${OPENAI_API_KEY:-sk-placeholder-replace-with-real-openai-key}"

  if [ "$ENV" = "dev" ]; then
    # Dev: use SSM Parameter Store (SecureString) to avoid Secrets Manager per-secret cost
    echo "  Using SSM Parameter Store for dev (no Secrets Manager cost)."
    aws ssm put-parameter --name "${SSM_PREFIX}/api-keys" --value "$API_KEYS_JSON" --type SecureString --overwrite --region "$REGION"
    echo "  OK: Created/updated SSM parameter ${SSM_PREFIX}/api-keys"
    echo "T-5.2.2: Create SSM parameter ${SSM_PREFIX}/openai-api-key..."
    aws ssm put-parameter --name "${SSM_PREFIX}/openai-api-key" --value "$OPENAI_KEY" --type SecureString --overwrite --region "$REGION"
    echo "  OK: Created/updated SSM parameter ${SSM_PREFIX}/openai-api-key"
  else
    # Staging/prod: use Secrets Manager
    SECRET_NAME="prompt-gateway/${ENV}/api-keys"
    if aws secretsmanager describe-secret --secret-id "$SECRET_NAME" --region "$REGION" 2>/dev/null; then
      aws secretsmanager put-secret-value --secret-id "$SECRET_NAME" --secret-string "$API_KEYS_JSON" --region "$REGION"
      echo "  OK: Updated existing secret $SECRET_NAME"
    else
      aws secretsmanager create-secret --name "$SECRET_NAME" --secret-string "$API_KEYS_JSON" --region "$REGION"
      echo "  OK: Created secret $SECRET_NAME"
    fi
    echo "T-5.2.2: Create secret prompt-gateway/${ENV}/openai-api-key..."
    OPENAI_SECRET_NAME="prompt-gateway/${ENV}/openai-api-key"
    if aws secretsmanager describe-secret --secret-id "$OPENAI_SECRET_NAME" --region "$REGION" 2>/dev/null; then
      aws secretsmanager put-secret-value --secret-id "$OPENAI_SECRET_NAME" --secret-string "$OPENAI_KEY" --region "$REGION"
      echo "  OK: Updated existing secret $OPENAI_SECRET_NAME"
    else
      aws secretsmanager create-secret --name "$OPENAI_SECRET_NAME" --secret-string "$OPENAI_KEY" --region "$REGION"
      echo "  OK: Created secret $OPENAI_SECRET_NAME"
    fi
  fi

  if [ "$API_KEYS_JSON" = '["change-me-dev-key"]' ]; then
    echo "  NOTE: Using placeholder API key. Set API_KEYS_JSON or update in AWS Console/SSM."
  fi
  if [ "$OPENAI_KEY" = "sk-placeholder-replace-with-real-openai-key" ]; then
    echo "  NOTE: Using placeholder OpenAI key. Set OPENAI_API_KEY or update in AWS Console/SSM."
  fi
  echo ""
else
  echo "Skipping secrets (--skip-secrets)"
  echo ""
fi

# --- T-5.2.3: Create SSM parameters (table name, queue URLs, bucket names) ---
if [ "$SKIP_SSM" = false ]; then
  echo "T-5.2.3: Create SSM parameters under $SSM_PREFIX..."

  # Ensure Phase 1 resources exist and get queue URLs
  DISPATCH_URL=$(aws sqs get-queue-url --queue-name "$DISPATCH_QUEUE" --region "$REGION" 2>/dev/null | jq -r '.QueueUrl // empty')
  RESULT_URL=$(aws sqs get-queue-url --queue-name "$RESULT_QUEUE" --region "$REGION" 2>/dev/null | jq -r '.QueueUrl // empty')
  if [ -z "$DISPATCH_URL" ] || [ -z "$RESULT_URL" ]; then
    echo "  FAIL: SQS queues not found. Run Phase 1 first."
    exit 1
  fi

  put_param() {
    aws ssm put-parameter --name "$1" --value "$2" --type String --overwrite --region "$REGION"
  }

  put_param "${SSM_PREFIX}/dynamodb-table-name"       "$TABLE_NAME"
  put_param "${SSM_PREFIX}/dynamodb-gsi-name"         "JobListIndex"
  put_param "${SSM_PREFIX}/dispatch-queue-url"         "$DISPATCH_URL"
  put_param "${SSM_PREFIX}/result-queue-url"          "$RESULT_URL"
  put_param "${SSM_PREFIX}/prompts-bucket"            "$PROMPTS_BUCKET"
  put_param "${SSM_PREFIX}/results-bucket"             "$RESULTS_BUCKET"
  echo "  OK: SSM parameters created/updated"
  echo ""
else
  echo "Skipping SSM (--skip-ssm)"
  echo ""
fi

# --- T-5.2.4: ECS task roles already have Secrets Manager + SSM permissions (Terraform IAM module) ---
echo "T-5.2.4: ECS task roles (control-plane-api, provider-worker) already have"
echo "         Secrets Manager and SSM GetParameter permissions via Terraform IAM module."
echo ""

echo "=== Phase 2 complete. ==="
echo ""
echo "Next: Phase 3 (Application deploy) - run ./scripts/first-deploy-phase3.sh --processing-mode ecs|lambda (see docs/DEPLOYMENT_PLAN.md T-5.3)"
