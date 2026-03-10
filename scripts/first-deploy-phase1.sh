#!/usr/bin/env bash
# First-deploy Phase 1: Infrastructure (T-5.1.1 – T-5.1.8)
# Deploys Terraform infrastructure for dev and verifies resources.
#
# Prerequisites:
#   - AWS credentials configured
#   - Terraform >= 1.0 installed
#   - Backend bootstrap: run ./scripts/bootstrap-terraform-backend.sh dev first
#   - dev.tfvars exists in infra/terraform/environments/dev/
#
# Usage: ./scripts/first-deploy-phase1.sh [--plan-only] [--skip-verify]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TF_DIR="$REPO_ROOT/infra/terraform/environments/dev"
ENV="dev"
PLAN_ONLY=false
SKIP_VERIFY=false

for arg in "$@"; do
  case $arg in
    --plan-only)  PLAN_ONLY=true ;;
    --skip-verify) SKIP_VERIFY=true ;;
  esac
done

echo "=== First-deploy Phase 1: Infrastructure (T-5.1) ==="
echo ""

# T-5.1.1: cd to dev environment
if [ ! -d "$TF_DIR" ]; then
  echo "ERROR: Terraform dev directory not found: $TF_DIR"
  exit 1
fi

if [ ! -f "$TF_DIR/dev.tfvars" ]; then
  echo "ERROR: dev.tfvars not found. Copy from dev.tfvars.example:"
  echo "  cp $TF_DIR/dev.tfvars.example $TF_DIR/dev.tfvars"
  exit 1
fi

cd "$TF_DIR"
echo "Working directory: $(pwd)"
echo ""

# T-5.1.2: terraform init
echo "T-5.1.2: terraform init"
terraform init
echo ""

# T-5.1.3: terraform plan
echo "T-5.1.3: terraform plan -var-file=dev.tfvars"
terraform plan -var-file=dev.tfvars -out=tfplan
echo ""

if [ "$PLAN_ONLY" = true ]; then
  echo "Plan-only mode. Exiting. Run without --plan-only to apply."
  exit 0
fi

# T-5.1.4: terraform apply (uses plan from T-5.1.3)
echo "T-5.1.4: terraform apply"
terraform apply -auto-approve tfplan
echo ""

if [ "$SKIP_VERIFY" = true ]; then
  echo "Skipping verification (--skip-verify)"
  exit 0
fi

# T-5.1.5 – T-5.1.8: Verification
echo "=== Verification (T-5.1.5 – T-5.1.8) ==="
tf_output_raw() {
  terraform output -raw "$1" 2>/dev/null || true
}

REGION="$(tf_output_raw aws_region)"
[ -z "$REGION" ] && REGION="${AWS_REGION:-us-east-1}"

TF_ENV="$(tf_output_raw environment_name)"
[ -z "$TF_ENV" ] && TF_ENV="$ENV"

TABLE="$(tf_output_raw dynamodb_table_name)"
[ -z "$TABLE" ] && TABLE="prompt-gateway-${TF_ENV}"

GSI_NAME="$(tf_output_raw dynamodb_gsi_name)"
[ -z "$GSI_NAME" ] && GSI_NAME="JobListIndex"

DISPATCH_URL_OUT="$(tf_output_raw dispatch_queue_url)"
RESULT_URL_OUT="$(tf_output_raw result_queue_url)"
DLQ_URL_OUT="$(tf_output_raw dlq_url)"

PROMPTS_BUCKET_OUT="$(tf_output_raw prompts_bucket_name)"
RESULTS_BUCKET_OUT="$(tf_output_raw results_bucket_name)"

CLUSTER_NAME_OUT="$(tf_output_raw ecs_cluster_name)"
[ -z "$CLUSTER_NAME_OUT" ] && CLUSTER_NAME_OUT="prompt-gateway-${TF_ENV}"

ECR_API_REPO_URL_OUT="$(tf_output_raw ecr_api_repo_url)"
ECR_WORKER_REPO_URL_OUT="$(tf_output_raw ecr_worker_repo_url)"
if [ -n "$ECR_API_REPO_URL_OUT" ]; then
  ECR_API_REPO_OUT="$(basename "$ECR_API_REPO_URL_OUT")"
else
  ECR_API_REPO_OUT="prompt-gateway-${TF_ENV}-control-plane-api"
fi
if [ -n "$ECR_WORKER_REPO_URL_OUT" ]; then
  ECR_WORKER_REPO_OUT="$(basename "$ECR_WORKER_REPO_URL_OUT")"
else
  ECR_WORKER_REPO_OUT="prompt-gateway-${TF_ENV}-provider-worker"
fi

# T-5.1.5: DynamoDB table exists, GSI present, TTL enabled
echo "T-5.1.5: Verify DynamoDB table..."
DESC=$(aws dynamodb describe-table --table-name "$TABLE" --region "$REGION" 2>/dev/null || true)
if [ -z "$DESC" ]; then
  echo "  FAIL: DynamoDB table $TABLE not found (region=$REGION)"
  exit 1
fi
GSI=$(echo "$DESC" | jq -r --arg gsi "$GSI_NAME" '.Table.GlobalSecondaryIndexes[]? | select(.IndexName==$gsi) | .IndexName' 2>/dev/null || true)
if [ "$GSI" != "$GSI_NAME" ]; then
  echo "  FAIL: GSI $GSI_NAME not found"
  exit 1
fi
# TTL is returned by describe-time-to-live, not describe-table
TTL_DESC=$(aws dynamodb describe-time-to-live --table-name "$TABLE" --region "$REGION" 2>/dev/null || true)
TTL=$(echo "$TTL_DESC" | jq -r '.TimeToLiveDescription.TimeToLiveStatus // empty' 2>/dev/null || true)
if [ "$TTL" != "ENABLED" ]; then
  echo "  FAIL: TTL not enabled (status: $TTL)"
  exit 1
fi
echo "  OK: DynamoDB table $TABLE, GSI $GSI_NAME, TTL enabled"
echo ""

# T-5.1.6: SQS queues exist, DLQ configured
echo "T-5.1.6: Verify SQS queues..."
if [ -n "$DISPATCH_URL_OUT" ] && [ -n "$RESULT_URL_OUT" ] && [ -n "$DLQ_URL_OUT" ]; then
  for URL in "$DISPATCH_URL_OUT" "$RESULT_URL_OUT" "$DLQ_URL_OUT"; do
    if ! aws sqs get-queue-attributes --queue-url "$URL" --attribute-names QueueArn --region "$REGION" >/dev/null 2>&1; then
      echo "  FAIL: Queue URL not found/reachable: $URL"
      exit 1
    fi
    echo "  OK: $(basename "$URL")"
  done
else
  for Q in "prompt-gateway-${TF_ENV}-dispatch" "prompt-gateway-${TF_ENV}-result" "prompt-gateway-${TF_ENV}-dlq"; do
    URL=$(aws sqs get-queue-url --queue-name "$Q" --region "$REGION" 2>/dev/null | jq -r '.QueueUrl // empty')
    if [ -z "$URL" ]; then
      echo "  FAIL: Queue $Q not found"
      exit 1
    fi
    echo "  OK: $Q"
  done
fi
echo ""

# T-5.1.7: S3 buckets exist
echo "T-5.1.7: Verify S3 buckets..."
ACCOUNT=$(aws sts get-caller-identity --query Account --output text 2>/dev/null)
PROMPTS="${PROMPTS_BUCKET_OUT:-prompt-gateway-${TF_ENV}-prompts-${ACCOUNT}}"
RESULTS="${RESULTS_BUCKET_OUT:-prompt-gateway-${TF_ENV}-results-${ACCOUNT}}"
for B in "$PROMPTS" "$RESULTS"; do
  if ! aws s3api head-bucket --bucket "$B" 2>/dev/null; then
    echo "  FAIL: Bucket $B not found"
    exit 1
  fi
  echo "  OK: $B"
done
echo ""

# T-5.1.8: ECR repos exist, ECS cluster created
# Note: describe-repositories can return exit 0 with empty list in some cases;
# we explicitly verify the repository exists by checking the response content.
echo "T-5.1.8: Verify ECR repos and ECS cluster..."
for R in "$ECR_API_REPO_OUT" "$ECR_WORKER_REPO_OUT"; do
  REPO_NAME=$(aws ecr describe-repositories --repository-names "$R" --region "$REGION" 2>/dev/null | jq -r '.repositories[0].repositoryName // ""') || REPO_NAME=""
  if [ -z "$REPO_NAME" ] || [ "$REPO_NAME" != "$R" ]; then
    echo "  FAIL: ECR repo $R not found"
    exit 1
  fi
  echo "  OK: ECR $R"
done
if ! aws ecs describe-clusters --clusters "$CLUSTER_NAME_OUT" --region "$REGION" 2>/dev/null | jq -e --arg c "$CLUSTER_NAME_OUT" '.clusters[0].clusterName == $c' >/dev/null 2>&1; then
  echo "  FAIL: ECS cluster $CLUSTER_NAME_OUT not found"
  exit 1
fi
echo "  OK: ECS cluster $CLUSTER_NAME_OUT"
echo ""

echo "=== Phase 1 complete. All verifications passed. ==="
echo ""
echo "Next: Phase 2 (Config & secrets) - run ./scripts/first-deploy-phase2.sh (see docs/DEPLOYMENT_PLAN.md T-5.2)"
