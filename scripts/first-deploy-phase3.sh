#!/usr/bin/env bash
# First-deploy Phase 3: Application deploy (T-5.3.1 – T-5.3.6)
# Builds and pushes the application artifacts for the selected processing mode,
# updates ECS task definitions, and verifies that the selected runtime is active
# while the unselected runtime is disabled.
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
#   - IMAGE_TAG: immutable tag for pushed images (default: current git SHA, or UTC timestamp fallback)
#   - AWS_REGION: default us-east-1
#   - PROCESSING_MODE: lambda (default) or ecs
#   - HTTP_EDGE_MODE: lambda (default) or ecs
#   - DEPLOY_ECS_API_SERVICE: auto (default), true, or false
#
# Usage: ./scripts/first-deploy-phase3.sh [--processing-mode lambda|ecs] [--build-only] [--skip-verify]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV="${ENV:-dev}"
REGION="${AWS_REGION:-us-east-1}"
PROCESSING_MODE="${PROCESSING_MODE:-lambda}"
HTTP_EDGE_MODE="${HTTP_EDGE_MODE:-lambda}"
DEPLOY_ECS_API_SERVICE="${DEPLOY_ECS_API_SERVICE:-auto}"
BUILD_ONLY=false
SKIP_VERIFY=false
DEFAULT_IMAGE_TAG="$(git -C "$REPO_ROOT" rev-parse --short=12 HEAD 2>/dev/null || date -u +%Y%m%d%H%M%S)"
IMAGE_TAG="${IMAGE_TAG:-$DEFAULT_IMAGE_TAG}"

while [ $# -gt 0 ]; do
  case "$1" in
    --processing-mode)
      PROCESSING_MODE="${2:-}"
      shift 2
      ;;
    --processing-mode=*)
      PROCESSING_MODE="${1#*=}"
      shift
      ;;
    --build-only)
      BUILD_ONLY=true
      shift
      ;;
    --skip-verify)
      SKIP_VERIFY=true
      shift
      ;;
    *)
      echo "ERROR: Unknown argument: $1"
      exit 1
      ;;
  esac
done

if [ "$PROCESSING_MODE" != "ecs" ] && [ "$PROCESSING_MODE" != "lambda" ]; then
  echo "ERROR: --processing-mode must be 'ecs' or 'lambda'."
  exit 1
fi

if [ "$HTTP_EDGE_MODE" != "ecs" ] && [ "$HTTP_EDGE_MODE" != "lambda" ]; then
  echo "ERROR: HTTP_EDGE_MODE must be 'ecs' or 'lambda'."
  exit 1
fi

if [ "$DEPLOY_ECS_API_SERVICE" != "auto" ] && [ "$DEPLOY_ECS_API_SERVICE" != "true" ] && [ "$DEPLOY_ECS_API_SERVICE" != "false" ]; then
  echo "ERROR: DEPLOY_ECS_API_SERVICE must be 'auto', 'true', or 'false'."
  exit 1
fi

if [ "$DEPLOY_ECS_API_SERVICE" = "auto" ]; then
  if [ "$PROCESSING_MODE" = "lambda" ] && [ "$HTTP_EDGE_MODE" = "lambda" ]; then
    DEPLOY_ECS_API_SERVICE="false"
  else
    DEPLOY_ECS_API_SERVICE="true"
  fi
fi

echo "=== First-deploy Phase 3: Application deploy (T-5.3) ==="
echo "Environment: $ENV  Region: $REGION  Image tag: $IMAGE_TAG  Processing mode: $PROCESSING_MODE  HTTP edge: $HTTP_EDGE_MODE  Deploy ECS API: $DEPLOY_ECS_API_SERVICE"
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
  -f "./${CONTROL_PLANE_CONTEXT}/ControlPlane.Api/Dockerfile" \
  "./${CONTROL_PLANE_CONTEXT}"
docker push "$ECR_REGISTRY/$ECR_REPO_API:$IMAGE_TAG"
echo "  OK: $ECR_REPO_API pushed"

if [ "$PROCESSING_MODE" = "ecs" ]; then
  echo ""
  echo "T-5.3.2: Build and push Provider Worker image"
  docker build --platform linux/amd64 \
    -t "$ECR_REGISTRY/$ECR_REPO_WORKER:$IMAGE_TAG" \
    -f "./${WORKER_CONTEXT}/Provider.Worker.Host/Dockerfile" \
    "./${WORKER_CONTEXT}"
  docker push "$ECR_REGISTRY/$ECR_REPO_WORKER:$IMAGE_TAG"
  echo "  OK: $ECR_REPO_WORKER pushed"
else
  echo ""
  echo "T-5.3.2: Skipping Provider Worker container image because Lambda mode is selected"
fi

if [ "$BUILD_ONLY" = true ]; then
  echo ""
  echo "=== Phase 3 (build only) complete. Skipping runtime deploy (--build-only). ==="
  exit 0
fi

MODE_SWITCH_ARGS=(--mode "$PROCESSING_MODE" --skip-verify)
if [ "$PROCESSING_MODE" = "ecs" ]; then
  echo ""
  echo "T-5.3.3: Switch processing mode to ECS"
else
  echo ""
  echo "T-5.3.3: Switch processing mode to Lambda"
fi
HTTP_EDGE_MODE="$HTTP_EDGE_MODE" "$REPO_ROOT/scripts/set-processing-mode.sh" "${MODE_SWITCH_ARGS[@]}"

# --- T-5.3.4: Create/update ECS task definitions with correct image URIs (and dev → SSM secrets) ---
echo ""
echo "T-5.3.4: Update and register ECS task definitions"
API_IMAGE="$ECR_REGISTRY/$ECR_REPO_API:$IMAGE_TAG"
WORKER_IMAGE="$ECR_REGISTRY/$ECR_REPO_WORKER:$IMAGE_TAG"
API_TASKDEF_FILE="$(mktemp "${TMPDIR:-/tmp}/api-taskdef.XXXXXX.json")"
WORKER_TASKDEF_FILE="$(mktemp "${TMPDIR:-/tmp}/worker-taskdef.XXXXXX.json")"
trap 'rm -f "$API_TASKDEF_FILE" "$WORKER_TASKDEF_FILE"' EXIT

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

if [ "$DEPLOY_ECS_API_SERVICE" = "true" ]; then
  aws ecs describe-task-definition --task-definition "$TASK_DEF_API" --region "$REGION" \
    | jq "$API_JQ_IMAGE" > "$API_TASKDEF_FILE"
fi

if [ "$PROCESSING_MODE" = "ecs" ]; then
  aws ecs describe-task-definition --task-definition "$TASK_DEF_WORKER" --region "$REGION" \
    | jq "$WORKER_JQ_IMAGE" > "$WORKER_TASKDEF_FILE"
fi

if [ "$DEPLOY_ECS_API_SERVICE" = "true" ]; then
  aws ecs register-task-definition --cli-input-json "file://$API_TASKDEF_FILE" --region "$REGION" > /dev/null
fi
if [ "$PROCESSING_MODE" = "ecs" ]; then
  aws ecs register-task-definition --cli-input-json "file://$WORKER_TASKDEF_FILE" --region "$REGION" > /dev/null
fi
echo "  OK: Task definitions registered"

# --- T-5.3.5 & T-5.3.6: Deploy services ---
echo ""
echo "T-5.3.5: Deploy Control Plane API service"
if [ "$DEPLOY_ECS_API_SERVICE" = "true" ]; then
  aws ecs update-service \
    --cluster "$CLUSTER" \
    --service "$API_SERVICE" \
    --task-definition "$TASK_DEF_API" \
    --force-new-deployment \
    --region "$REGION" \
    --no-cli-pager --query 'service.serviceName' --output text
else
  echo "  Skipping ECS control-plane-api redeploy because Lambda HTTP mode keeps the ECS API service off by default"
fi

if [ "$PROCESSING_MODE" = "ecs" ]; then
  echo ""
  echo "T-5.3.6: Deploy Provider Worker service"
  aws ecs update-service \
    --cluster "$CLUSTER" \
    --service "$WORKER_SERVICE" \
    --task-definition "$TASK_DEF_WORKER" \
    --force-new-deployment \
    --region "$REGION" \
    --no-cli-pager --query 'service.serviceName' --output text
fi

# --- T-5.3.7: Verify selected runtime is active ---
if [ "$SKIP_VERIFY" = true ]; then
  echo ""
  echo "Skipping wait (--skip-verify). Services are updating."
else
  echo ""
  echo "T-5.3.7: Wait for services to stabilize"
  aws ecs wait services-stable \
    --cluster "$CLUSTER" \
    --services "$API_SERVICE" "$WORKER_SERVICE" \
    --region "$REGION"
  HTTP_EDGE_MODE="$HTTP_EDGE_MODE" "$REPO_ROOT/scripts/set-processing-mode.sh" --mode "$PROCESSING_MODE" --verify-only
fi

echo ""
echo "=== Phase 3 complete. ==="
echo ""
echo "Next: Phase 4 (Smoke tests) - run ./scripts/first-deploy-phase4.sh (see docs/DEPLOYMENT_PLAN.md T-5.4)"
