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
REGION="${AWS_REGION:-us-east-1}"

# T-5.1.5: DynamoDB table exists, GSI present, TTL enabled
echo "T-5.1.5: Verify DynamoDB table..."
TABLE="prompt-gateway-dev"
DESC=$(aws dynamodb describe-table --table-name "$TABLE" --region "$REGION" 2>/dev/null || true)
if [ -z "$DESC" ]; then
  echo "  FAIL: DynamoDB table $TABLE not found"
  exit 1
fi
GSI=$(echo "$DESC" | jq -r '.Table.GlobalSecondaryIndexes[]? | select(.IndexName=="JobListIndex") | .IndexName' 2>/dev/null || true)
if [ "$GSI" != "JobListIndex" ]; then
  echo "  FAIL: GSI JobListIndex not found"
  exit 1
fi
# TTL is returned by describe-time-to-live, not describe-table
TTL_DESC=$(aws dynamodb describe-time-to-live --table-name "$TABLE" --region "$REGION" 2>/dev/null || true)
TTL=$(echo "$TTL_DESC" | jq -r '.TimeToLiveDescription.TimeToLiveStatus // empty' 2>/dev/null || true)
if [ "$TTL" != "ENABLED" ]; then
  echo "  FAIL: TTL not enabled (status: $TTL)"
  exit 1
fi
echo "  OK: DynamoDB table $TABLE, GSI JobListIndex, TTL enabled"
echo ""

# T-5.1.6: SQS queues exist, DLQ configured
echo "T-5.1.6: Verify SQS queues..."
for Q in "prompt-gateway-dev-dispatch" "prompt-gateway-dev-result" "prompt-gateway-dev-dlq"; do
  URL=$(aws sqs get-queue-url --queue-name "$Q" --region "$REGION" 2>/dev/null | jq -r '.QueueUrl // empty')
  if [ -z "$URL" ]; then
    echo "  FAIL: Queue $Q not found"
    exit 1
  fi
  echo "  OK: $Q"
done
echo ""

# T-5.1.7: S3 buckets exist
echo "T-5.1.7: Verify S3 buckets..."
ACCOUNT=$(aws sts get-caller-identity --query Account --output text 2>/dev/null)
PROMPTS="prompt-gateway-dev-prompts-${ACCOUNT}"
RESULTS="prompt-gateway-dev-results-${ACCOUNT}"
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
for R in "prompt-gateway-dev-control-plane-api" "prompt-gateway-dev-provider-worker"; do
  REPO_NAME=$(aws ecr describe-repositories --repository-names "$R" --region "$REGION" 2>/dev/null | jq -r '.repositories[0].repositoryName // ""') || REPO_NAME=""
  if [ -z "$REPO_NAME" ] || [ "$REPO_NAME" != "$R" ]; then
    echo "  FAIL: ECR repo $R not found"
    exit 1
  fi
  echo "  OK: ECR $R"
done
if ! aws ecs describe-clusters --clusters "prompt-gateway-dev" --region "$REGION" 2>/dev/null | jq -e '.clusters[0].clusterName == "prompt-gateway-dev"' >/dev/null 2>&1; then
  echo "  FAIL: ECS cluster prompt-gateway-dev not found"
  exit 1
fi
echo "  OK: ECS cluster prompt-gateway-dev"
echo ""

echo "=== Phase 1 complete. All verifications passed. ==="
echo ""
echo "Next: Phase 2 (Config & secrets) - run ./scripts/first-deploy-phase2.sh (see docs/DEPLOYMENT_PLAN.md T-5.2)"
