#!/usr/bin/env bash
# First-deploy Phase 4: Smoke tests (T-5.4.1 – T-5.4.5)
# Runs health, ready, POST /jobs, poll job status, GET result via scripts/smoke-test.sh.
# This works after either ECS processing mode or Lambda processing mode is deployed.
#
# Prerequisites:
#   - Phase 1 and 2 complete
#   - Phase 3 complete for ECS mode, or Phase 3/5 complete for Lambda mode
#   - AWS credentials configured
#   - jq installed
#
# Optional env vars:
#   - ENV: dev (default), staging, or prod
#   - HTTP_EDGE_MODE: lambda (default) or ecs
#   - VERIFY_ECS_HTTP_STATE: when HTTP_EDGE_MODE=lambda, require the ECS API service to be off; when HTTP_EDGE_MODE=ecs, require it to be on (default: true)
#   - BASE_URL: override (e.g. https://api.example.com); otherwise derived from ALB
#   - API_KEY: override; otherwise fetched from SSM (dev) or Secrets Manager (staging/prod)
#   - HEALTH_CHECK_BASE_URL: same as BASE_URL; used when set to avoid --insecure for custom domains
#   - SMOKE_INPUT_REF: prompt key or s3://bucket/key override (default: prompts/smoke-test.txt)
#   - SMOKE_PROMPT_BUCKET: explicit prompt bucket override when SMOKE_INPUT_REF is not an S3 URI
#   - SMOKE_PROMPT_TEXT: prompt body uploaded before the smoke test
#   - SMOKE_SKIP_PROMPT_UPLOAD=true: skip uploading the smoke prompt fixture
#   - AWS_REGION: default us-east-1
#
# Usage: ./scripts/first-deploy-phase4.sh [--insecure]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV="${ENV:-dev}"
REGION="${AWS_REGION:-us-east-1}"
HTTP_EDGE_MODE="${HTTP_EDGE_MODE:-lambda}"
USE_INSECURE=""
[ "${1:-}" = "--insecure" ] && USE_INSECURE="--insecure"
INPUT_REF="${SMOKE_INPUT_REF:-prompts/smoke-test.txt}"
SMOKE_PROMPT_TEXT="${SMOKE_PROMPT_TEXT:-Reply with exactly: smoke test ok}"
SMOKE_SKIP_PROMPT_UPLOAD="${SMOKE_SKIP_PROMPT_UPLOAD:-false}"
VERIFY_ECS_HTTP_STATE="${VERIFY_ECS_HTTP_STATE:-true}"

echo "=== First-deploy Phase 4: Smoke tests (T-5.4) ==="
echo "Environment: $ENV  Region: $REGION"
echo ""

# Resolve BASE_URL if not set
if [ -z "${BASE_URL:-}" ]; then
  BASE_URL="${HEALTH_CHECK_BASE_URL:-}"
fi
if [ -z "$BASE_URL" ]; then
  if [ "$HTTP_EDGE_MODE" = "lambda" ]; then
    API_ID=$(aws apigatewayv2 get-apis \
      --region "$REGION" \
      --query "Items[?Name=='prompt-gateway-${ENV}-control-plane-http'].ApiId | [0]" \
      --output text 2>/dev/null || true)
    if [ -z "$API_ID" ] || [ "$API_ID" = "None" ]; then
      echo "Error: Could not resolve API Gateway HTTP API for prompt-gateway-${ENV}-control-plane-http. Set BASE_URL or disable HTTP_EDGE_MODE=lambda."
      exit 1
    fi
    BASE_URL=$(aws apigatewayv2 get-api \
      --api-id "$API_ID" \
      --region "$REGION" \
      --query 'ApiEndpoint' \
      --output text 2>/dev/null || true)
    if [ -z "$BASE_URL" ] || [ "$BASE_URL" = "None" ]; then
      echo "Error: Could not resolve API Gateway endpoint for $API_ID."
      exit 1
    fi
  else
    ALB_DNS=$(aws elbv2 describe-load-balancers \
      --names "prompt-gateway-${ENV}" \
      --region "$REGION" \
      --query 'LoadBalancers[0].DNSName' \
      --output text 2>/dev/null || true)
    if [ -z "$ALB_DNS" ] || [ "$ALB_DNS" = "None" ]; then
      echo "Error: Could not resolve ALB DNS for prompt-gateway-${ENV}. Set BASE_URL or HEALTH_CHECK_BASE_URL."
      exit 1
    fi
    if [ "$(echo "$ENV" | tr '[:upper:]' '[:lower:]')" = "dev" ]; then
      BASE_URL="http://$ALB_DNS"
    else
      BASE_URL="https://$ALB_DNS"
      [ -z "${HEALTH_CHECK_BASE_URL:-}" ] && USE_INSECURE="--insecure"
    fi
  fi
fi
BASE_URL="${BASE_URL%/}"
echo "BASE_URL: $BASE_URL"

# Resolve API_KEY if not set
if [ -z "${API_KEY:-}" ]; then
  if [ "$(echo "$ENV" | tr '[:upper:]' '[:lower:]')" = "dev" ]; then
    API_KEY=$(aws ssm get-parameter --name "/prompt-gateway/${ENV}/api-keys" --with-decryption \
      --region "$REGION" --query Parameter.Value --output text 2>/dev/null | jq -r '.[0] // empty' || true)
  fi
  if [ -z "$API_KEY" ]; then
    API_KEY=$(aws secretsmanager get-secret-value --secret-id "prompt-gateway/${ENV}/api-keys" \
      --region "$REGION" --query SecretString --output text 2>/dev/null | jq -r '.[0] // empty' || true)
  fi
fi
if [ -z "$API_KEY" ]; then
  echo "Error: No API key. Set API_KEY or ensure SSM /prompt-gateway/${ENV}/api-keys or Secrets Manager prompt-gateway/${ENV}/api-keys exists."
  exit 1
fi

if [ "$SMOKE_SKIP_PROMPT_UPLOAD" != "true" ]; then
  ACCOUNT=$(aws sts get-caller-identity --query Account --output text --region "$REGION" 2>/dev/null || true)
  if [ -z "$ACCOUNT" ]; then
    echo "Error: Could not resolve AWS account for smoke prompt upload."
    exit 1
  fi

  PROMPT_BUCKET="${SMOKE_PROMPT_BUCKET:-}"
  PROMPT_KEY="$INPUT_REF"
  if [[ "$INPUT_REF" =~ ^s3://([^/]+)/(.+)$ ]]; then
    PROMPT_BUCKET="${BASH_REMATCH[1]}"
    PROMPT_KEY="${BASH_REMATCH[2]}"
  fi

  if [ -z "$PROMPT_BUCKET" ]; then
    PROMPT_BUCKET="prompt-gateway-${ENV}-prompts-${ACCOUNT}"
  fi

  TMP_PROMPT_FILE="$(mktemp -t smoke-prompt)"
  trap 'rm -f "$TMP_PROMPT_FILE"' EXIT
  printf '%s\n' "$SMOKE_PROMPT_TEXT" > "$TMP_PROMPT_FILE"

  echo "Uploading smoke prompt fixture to s3://$PROMPT_BUCKET/$PROMPT_KEY"
  aws s3api put-object \
    --bucket "$PROMPT_BUCKET" \
    --key "$PROMPT_KEY" \
    --body "$TMP_PROMPT_FILE" \
    --content-type "text/plain; charset=utf-8" \
    --region "$REGION" >/dev/null
  echo ""
fi

verify_ecs_http_state() {
  if [ "$VERIFY_ECS_HTTP_STATE" != "true" ]; then
    return
  fi

  local ecs_status
  ecs_status=$(aws ecs describe-services \
    --cluster "prompt-gateway-${ENV}" \
    --services control-plane-api provider-worker \
    --region "$REGION" \
    --query 'services[].{name:serviceName,desired:desiredCount,running:runningCount}' \
    --output json 2>/dev/null || true)

  if [ -z "$ecs_status" ] || [ "$ecs_status" = "[]" ]; then
    echo "FAIL: Could not inspect ECS fallback services after Lambda-edge smoke test."
    exit 1
  fi

  local api_desired
  local api_running
  local worker_desired
  local worker_running
  api_desired=$(echo "$ecs_status" | jq -r '.[] | select(.name=="control-plane-api") | .desired // empty')
  api_running=$(echo "$ecs_status" | jq -r '.[] | select(.name=="control-plane-api") | .running // empty')
  worker_desired=$(echo "$ecs_status" | jq -r '.[] | select(.name=="provider-worker") | .desired // empty')
  worker_running=$(echo "$ecs_status" | jq -r '.[] | select(.name=="provider-worker") | .running // empty')

  if [ -z "$api_desired" ] || [ -z "$api_running" ]; then
    echo "FAIL: ECS control-plane-api service metadata is missing."
    exit 1
  fi

  if [ "$HTTP_EDGE_MODE" = "lambda" ]; then
    if [ "$api_desired" != "0" ] || [ "$api_running" != "0" ]; then
      echo "FAIL: ECS control-plane-api should be off while Lambda HTTP edge is active (desired=$api_desired running=$api_running)."
      exit 1
    fi
    echo "ECS HTTP state check: control-plane-api desired=$api_desired running=$api_running"
  else
    if [ "$api_desired" -lt 1 ] || [ "$api_running" -lt 1 ]; then
      echo "FAIL: ECS control-plane-api should be on while ECS HTTP edge is active (desired=$api_desired running=$api_running)."
      exit 1
    fi
    echo "ECS HTTP state check: control-plane-api desired=$api_desired running=$api_running"
  fi
  if [ -n "$worker_desired" ] && [ -n "$worker_running" ]; then
    echo "ECS worker rollback state: provider-worker desired=$worker_desired running=$worker_running"
  fi
}

# T-5.4.1 – T-5.4.5: run smoke-test.sh (GET /health, GET /ready, POST /jobs, poll, GET /result)
cd "$REPO_ROOT"
chmod +x scripts/smoke-test.sh
if [ -n "$USE_INSECURE" ]; then
  SMOKE_INPUT_REF="$INPUT_REF" ./scripts/smoke-test.sh "$BASE_URL" "$API_KEY" "$USE_INSECURE"
else
  SMOKE_INPUT_REF="$INPUT_REF" ./scripts/smoke-test.sh "$BASE_URL" "$API_KEY"
fi

verify_ecs_http_state
