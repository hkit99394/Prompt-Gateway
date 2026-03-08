#!/usr/bin/env bash
# First-deploy Phase 3: Application deploy (T-5.3.1 – T-5.3.6)
# Builds and pushes Control Plane API and Provider Worker images to ECR,
# updates ECS task definitions, deploys both services, and verifies they are running.
#
# Prerequisites:
#   - AWS credentials configured
#   - Phase 1 complete (Terraform apply; ECR repos, ECS cluster, task definitions exist)
#   - Phase 2 complete (secrets/SSM in place)
#   - Docker installed and running
#   - jq installed
#
# If ECS fails with "secret was marked for deletion" (dev): the running task definition
# revision still points at Secrets Manager. Re-apply Terraform for dev, then force a new
# deployment so the service uses the Terraform revision (SSM). See docs/DEPLOYMENT_PLAN.md.
#
# Optional env vars:
#   - ENV: dev (default), staging, or prod
#   - IMAGE_TAG: tag for pushed images (default: latest)
#   - AWS_REGION: default us-east-1
#
# Usage: ./scripts/first-deploy-phase3.sh [--build-only] [--skip-verify]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV="${ENV:-dev}"
REGION="${AWS_REGION:-us-east-1}"
IMAGE_TAG="${IMAGE_TAG:-latest}"
BUILD_ONLY=false
SKIP_VERIFY=false

for arg in "$@"; do
  case $arg in
    --build-only)   BUILD_ONLY=true ;;
    --skip-verify)  SKIP_VERIFY=true ;;
  esac
done

echo "=== First-deploy Phase 3: Application deploy (T-5.3) ==="
echo "Environment: $ENV  Region: $REGION  Image tag: $IMAGE_TAG"
echo ""

# Prerequisite: Docker installed and daemon running
if ! command -v docker &>/dev/null; then
  echo "Error: Docker is not installed or not in PATH."
  echo "Phase 3 builds and pushes container images; Docker is required."
  echo "Install: https://docs.docker.com/get-docker/ (macOS: Docker Desktop or 'brew install docker')"
  exit 1
fi
if ! docker info &>/dev/null; then
  echo "Error: Docker daemon is not running."
  echo "Start Docker Desktop (or run 'sudo systemctl start docker' on Linux)."
  exit 1
fi

# Resource names (must match Terraform / Phase 1)
ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
ECR_REGISTRY="${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com"
ECR_REPO_API="prompt-gateway-${ENV}-control-plane-api"
ECR_REPO_WORKER="prompt-gateway-${ENV}-provider-worker"
CLUSTER="prompt-gateway-${ENV}"
API_SERVICE="control-plane-api"
WORKER_SERVICE="provider-worker"
TASK_DEF_API="prompt-gateway-${ENV}-control-plane-api"
TASK_DEF_WORKER="prompt-gateway-${ENV}-provider-worker"

CONTROL_PLANE_CONTEXT="Prompt Gateway – Control Plane /src"
WORKER_CONTEXT="Prompt Gateway Provider - OpenAI/src"

cd "$REPO_ROOT"

# --- ECR login ---
echo "Logging in to ECR..."
aws ecr get-login-password --region "$REGION" \
  | docker login --username AWS --password-stdin "$ECR_REGISTRY"

# --- T-5.3.1: Build and push Control Plane API image ---
echo ""
echo "T-5.3.1: Build and push Control Plane API image to ECR"
docker build --platform linux/amd64 \
  -t "$ECR_REGISTRY/$ECR_REPO_API:$IMAGE_TAG" \
  -t "$ECR_REGISTRY/$ECR_REPO_API:latest" \
  -f "./${CONTROL_PLANE_CONTEXT}/ControlPlane.Api/Dockerfile" \
  "./${CONTROL_PLANE_CONTEXT}"
docker push "$ECR_REGISTRY/$ECR_REPO_API:$IMAGE_TAG"
docker push "$ECR_REGISTRY/$ECR_REPO_API:latest"
echo "  OK: $ECR_REPO_API pushed"

# --- T-5.3.2: Build and push Provider Worker image ---
echo ""
echo "T-5.3.2: Build and push Provider Worker image"
docker build --platform linux/amd64 \
  -t "$ECR_REGISTRY/$ECR_REPO_WORKER:$IMAGE_TAG" \
  -t "$ECR_REGISTRY/$ECR_REPO_WORKER:latest" \
  -f "./${WORKER_CONTEXT}/Provider.Worker.Host/Dockerfile" \
  "./${WORKER_CONTEXT}"
docker push "$ECR_REGISTRY/$ECR_REPO_WORKER:$IMAGE_TAG"
docker push "$ECR_REGISTRY/$ECR_REPO_WORKER:latest"
echo "  OK: $ECR_REPO_WORKER pushed"

if [ "$BUILD_ONLY" = true ]; then
  echo ""
  echo "=== Phase 3 (build only) complete. Skipping ECS deploy (--build-only). ==="
  exit 0
fi

# --- T-5.3.3: Create/update ECS task definitions with correct image URIs (and dev → SSM secrets) ---
echo ""
echo "T-5.3.3: Update and register ECS task definitions"
API_IMAGE="$ECR_REGISTRY/$ECR_REPO_API:$IMAGE_TAG"
WORKER_IMAGE="$ECR_REGISTRY/$ECR_REPO_WORKER:$IMAGE_TAG"

# Dev uses SSM for secrets; override so we don't inherit a revision that still points at Secrets Manager
if [ "$(echo "$ENV" | tr '[:upper:]' '[:lower:]')" = "dev" ]; then
  API_KEYS_VALUE_FROM="arn:aws:ssm:${REGION}:${ACCOUNT}:parameter/prompt-gateway/${ENV}/api-keys"
  OPENAI_VALUE_FROM="arn:aws:ssm:${REGION}:${ACCOUNT}:parameter/prompt-gateway/${ENV}/openai-api-key"
  API_JQ_IMAGE=".taskDefinition | del(.taskDefinitionArn, .revision, .status, .requiresAttributes, .compatibilities, .registeredAt, .registeredBy) | .containerDefinitions[0].image = \"$API_IMAGE\" | .containerDefinitions[0].secrets = [{\"name\":\"ApiSecurity__ApiKey\",\"valueFrom\":\"$API_KEYS_VALUE_FROM\"}]"
  WORKER_JQ_IMAGE=".taskDefinition | del(.taskDefinitionArn, .revision, .status, .requiresAttributes, .compatibilities, .registeredAt, .registeredBy) | .containerDefinitions[0].image = \"$WORKER_IMAGE\" | .containerDefinitions[0].secrets = [{\"name\":\"ProviderWorker__OpenAi__ApiKey\",\"valueFrom\":\"$OPENAI_VALUE_FROM\"}]"
else
  API_JQ_IMAGE=".taskDefinition | del(.taskDefinitionArn, .revision, .status, .requiresAttributes, .compatibilities, .registeredAt, .registeredBy) | .containerDefinitions[0].image = \"$API_IMAGE\""
  WORKER_JQ_IMAGE=".taskDefinition | del(.taskDefinitionArn, .revision, .status, .requiresAttributes, .compatibilities, .registeredAt, .registeredBy) | .containerDefinitions[0].image = \"$WORKER_IMAGE\""
fi

aws ecs describe-task-definition --task-definition "$TASK_DEF_API" --region "$REGION" \
  | jq "$API_JQ_IMAGE" > /tmp/api-taskdef.json
aws ecs describe-task-definition --task-definition "$TASK_DEF_WORKER" --region "$REGION" \
  | jq "$WORKER_JQ_IMAGE" > /tmp/worker-taskdef.json

aws ecs register-task-definition --cli-input-json file:///tmp/api-taskdef.json --region "$REGION" > /dev/null
aws ecs register-task-definition --cli-input-json file:///tmp/worker-taskdef.json --region "$REGION" > /dev/null
echo "  OK: Task definitions registered"

# --- T-5.3.4 & T-5.3.5: Deploy Control Plane API and Provider Worker services ---
echo ""
echo "T-5.3.4: Deploy Control Plane API service"
aws ecs update-service \
  --cluster "$CLUSTER" \
  --service "$API_SERVICE" \
  --task-definition "$TASK_DEF_API" \
  --force-new-deployment \
  --region "$REGION" \
  --no-cli-pager --query 'service.serviceName' --output text

echo ""
echo "T-5.3.5: Deploy Provider Worker service"
aws ecs update-service \
  --cluster "$CLUSTER" \
  --service "$WORKER_SERVICE" \
  --task-definition "$TASK_DEF_WORKER" \
  --force-new-deployment \
  --region "$REGION" \
  --no-cli-pager --query 'service.serviceName' --output text

# --- T-5.3.6: Verify tasks are running ---
if [ "$SKIP_VERIFY" = true ]; then
  echo ""
  echo "Skipping wait (--skip-verify). Services are updating."
else
  echo ""
  echo "T-5.3.6: Wait for services to stabilize"
  aws ecs wait services-stable \
    --cluster "$CLUSTER" \
    --services "$API_SERVICE" "$WORKER_SERVICE" \
    --region "$REGION"
  echo "  OK: Services stable"
fi

echo ""
echo "=== Phase 3 complete. ==="
echo ""
echo "Next: Phase 4 (Smoke tests) - curl /health, /ready, POST /jobs, etc. See docs/DEPLOYMENT_PLAN.md T-5.4"
echo "  Or run: ./scripts/smoke-test.sh <BASE_URL> <API_KEY>"
