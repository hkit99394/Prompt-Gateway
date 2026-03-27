#!/usr/bin/env bash
# Switch queue processing between ECS and Lambda for a target environment.
# This updates Terraform, optionally packages Lambda artifacts, and verifies that
# the selected runtime is active while the unselected runtime is disabled.
#
# Prerequisites:
#   - AWS credentials configured
#   - Terraform >= 1.0 installed
#   - <env>.tfvars exists in infra/terraform/environments/<env>/
#
# Optional env vars:
#   - ENV: dev (default), staging, or prod
#   - AWS_REGION: default us-east-1
#   - HTTP_EDGE_MODE: lambda (default) or ecs
#   - LAMBDA_RUNTIME: override managed runtime for Lambda mode
#   - PROVIDER_LAMBDA_PACKAGE_PATH: override provider zip path
#   - RESULT_LAMBDA_PACKAGE_PATH: override result zip path
#   - OUTBOX_LAMBDA_PACKAGE_PATH: override outbox zip path
#   - CONTROL_PLANE_HTTP_LAMBDA_PACKAGE_PATH: override HTTP API Lambda zip path
#   - SMOKE_TEST_INSECURE=true: pass --insecure when running the optional smoke-test gate
#
# Usage:
#   ./scripts/set-processing-mode.sh --mode ecs [--plan-only] [--skip-verify] [--run-smoke-test]
#   ./scripts/set-processing-mode.sh --mode lambda [--plan-only] [--skip-package] [--skip-verify] [--run-smoke-test]
#   ./scripts/set-processing-mode.sh --mode ecs --verify-only [--run-smoke-test]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV="${ENV:-dev}"
REGION="${AWS_REGION:-us-east-1}"
HTTP_EDGE_MODE="${HTTP_EDGE_MODE:-lambda}"
MODE=""
PLAN_ONLY=false
SKIP_PACKAGE=false
SKIP_VERIFY=false
VERIFY_ONLY=false
RUN_SMOKE_TEST=false

while [ $# -gt 0 ]; do
  case "$1" in
    --mode)
      MODE="${2:-}"
      shift 2
      ;;
    --mode=*)
      MODE="${1#*=}"
      shift
      ;;
    --plan-only)
      PLAN_ONLY=true
      shift
      ;;
    --skip-package)
      SKIP_PACKAGE=true
      shift
      ;;
    --skip-verify)
      SKIP_VERIFY=true
      shift
      ;;
    --verify-only)
      VERIFY_ONLY=true
      shift
      ;;
    --run-smoke-test)
      RUN_SMOKE_TEST=true
      shift
      ;;
    *)
      echo "ERROR: Unknown argument: $1"
      exit 1
      ;;
  esac
done

if [ "$MODE" != "ecs" ] && [ "$MODE" != "lambda" ]; then
  echo "ERROR: --mode must be 'ecs' or 'lambda'."
  exit 1
fi

if [ "$HTTP_EDGE_MODE" != "ecs" ] && [ "$HTTP_EDGE_MODE" != "lambda" ]; then
  echo "ERROR: HTTP_EDGE_MODE must be 'ecs' or 'lambda'."
  exit 1
fi

TF_DIR="$REPO_ROOT/infra/terraform/environments/$ENV"
TFVARS_FILE="$TF_DIR/${ENV}.tfvars"
ENABLE_LAMBDA_PROCESSING="false"
ENABLE_LAMBDA_HTTP_API="false"

if [ "$MODE" = "lambda" ]; then
  ENABLE_LAMBDA_PROCESSING="true"
fi

if [ "$HTTP_EDGE_MODE" = "lambda" ]; then
  ENABLE_LAMBDA_HTTP_API="true"
fi

PROVIDER_LAMBDA_NAME="prompt-gateway-${ENV}-provider-worker"
RESULT_LAMBDA_NAME="prompt-gateway-${ENV}-result-ingestion"
OUTBOX_LAMBDA_NAME="prompt-gateway-${ENV}-outbox-dispatch"
CONTROL_PLANE_HTTP_LAMBDA_NAME="prompt-gateway-${ENV}-control-plane-http"
CONTROL_PLANE_HTTP_API_NAME="prompt-gateway-${ENV}-control-plane-http"
OUTBOX_RULE_NAME="prompt-gateway-${ENV}-outbox-dispatch"
CLUSTER_NAME="prompt-gateway-${ENV}"
API_SERVICE_NAME="control-plane-api"
WORKER_SERVICE_NAME="provider-worker"

require_env_dir() {
  if [ ! -d "$TF_DIR" ]; then
    echo "ERROR: Terraform environment directory not found: $TF_DIR"
    exit 1
  fi

  if [ ! -f "$TFVARS_FILE" ]; then
    echo "ERROR: ${ENV}.tfvars not found. Copy from ${ENV}.tfvars.example first."
    exit 1
  fi
}

lambda_function_exists() {
  local function_name="$1"
  aws lambda get-function --function-name "$function_name" --region "$REGION" >/dev/null 2>&1
}

event_rule_exists() {
  local rule_name="$1"
  aws events describe-rule --name "$rule_name" --region "$REGION" >/dev/null 2>&1
}

http_api_id() {
  aws apigatewayv2 get-apis \
    --region "$REGION" \
    --query "Items[?Name=='${CONTROL_PLANE_HTTP_API_NAME}'].ApiId | [0]" \
    --output text 2>/dev/null || true
}

http_api_exists() {
  local api_id
  api_id="$(http_api_id)"
  [ -n "$api_id" ] && [ "$api_id" != "None" ]
}

run_smoke_test_gate() {
  local smoke_args=()

  echo "Running smoke-test gate for processing=$MODE http_edge=$HTTP_EDGE_MODE..."
  if [ "${SMOKE_TEST_INSECURE:-false}" = "true" ]; then
    smoke_args+=("--insecure")
  fi

  if [ ${#smoke_args[@]} -gt 0 ]; then
    ENV="$ENV" AWS_REGION="$REGION" HTTP_EDGE_MODE="$HTTP_EDGE_MODE" "$REPO_ROOT/scripts/first-deploy-phase4.sh" "${smoke_args[@]}"
  else
    ENV="$ENV" AWS_REGION="$REGION" HTTP_EDGE_MODE="$HTTP_EDGE_MODE" "$REPO_ROOT/scripts/first-deploy-phase4.sh"
  fi
}

verify_mode() {
  local api_task_definition
  local api_desired_count
  local api_running_count
  local api_outbox_enabled
  local api_result_enabled
  local worker_desired_count
  local worker_running_count

  echo "Verifying $MODE processing mode..."

  aws ecs wait services-stable \
    --cluster "$CLUSTER_NAME" \
    --services "$API_SERVICE_NAME" "$WORKER_SERVICE_NAME" \
    --region "$REGION"

  api_task_definition=$(aws ecs describe-services \
    --cluster "$CLUSTER_NAME" \
    --services "$API_SERVICE_NAME" \
    --region "$REGION" \
    --query 'services[0].taskDefinition' \
    --output text)
  api_desired_count=$(aws ecs describe-services \
    --cluster "$CLUSTER_NAME" \
    --services "$API_SERVICE_NAME" \
    --region "$REGION" \
    --query 'services[0].desiredCount' \
    --output text)
  api_running_count=$(aws ecs describe-services \
    --cluster "$CLUSTER_NAME" \
    --services "$API_SERVICE_NAME" \
    --region "$REGION" \
    --query 'services[0].runningCount' \
    --output text)

  api_outbox_enabled=$(aws ecs describe-task-definition \
    --task-definition "$api_task_definition" \
    --region "$REGION" \
    --query 'taskDefinition.containerDefinitions[0].environment[?name==`HostedWorkers__EnableOutboxWorker`].value | [0]' \
    --output text)
  api_result_enabled=$(aws ecs describe-task-definition \
    --task-definition "$api_task_definition" \
    --region "$REGION" \
    --query 'taskDefinition.containerDefinitions[0].environment[?name==`HostedWorkers__EnableResultQueueWorker`].value | [0]' \
    --output text)
  worker_desired_count=$(aws ecs describe-services \
    --cluster "$CLUSTER_NAME" \
    --services "$WORKER_SERVICE_NAME" \
    --region "$REGION" \
    --query 'services[0].desiredCount' \
    --output text)
  worker_running_count=$(aws ecs describe-services \
    --cluster "$CLUSTER_NAME" \
    --services "$WORKER_SERVICE_NAME" \
    --region "$REGION" \
    --query 'services[0].runningCount' \
    --output text)

  if [ "$MODE" = "lambda" ]; then
    if [ "$api_outbox_enabled" != "false" ]; then
      echo "FAIL: API outbox worker is still enabled (value=$api_outbox_enabled)"
      exit 1
    fi
    if [ "$api_result_enabled" != "false" ]; then
      echo "FAIL: API result queue worker is still enabled (value=$api_result_enabled)"
      exit 1
    fi
    if [ "$worker_desired_count" != "0" ]; then
      echo "FAIL: Provider ECS worker desired count is not zero (value=$worker_desired_count)"
      exit 1
    fi
    if [ "$worker_running_count" != "0" ]; then
      echo "FAIL: Provider ECS worker is still running tasks (runningCount=$worker_running_count)"
      exit 1
    fi
    if ! lambda_function_exists "$PROVIDER_LAMBDA_NAME"; then
      echo "FAIL: Provider Lambda function is missing"
      exit 1
    fi
    if ! lambda_function_exists "$RESULT_LAMBDA_NAME"; then
      echo "FAIL: Result Lambda function is missing"
      exit 1
    fi
    if ! lambda_function_exists "$OUTBOX_LAMBDA_NAME"; then
      echo "FAIL: Outbox Lambda function is missing"
      exit 1
    fi

    provider_mapping_state=$(aws lambda list-event-source-mappings \
      --function-name "$PROVIDER_LAMBDA_NAME" \
      --region "$REGION" \
      --query 'EventSourceMappings[0].State' \
      --output text)
    result_mapping_state=$(aws lambda list-event-source-mappings \
      --function-name "$RESULT_LAMBDA_NAME" \
      --region "$REGION" \
      --query 'EventSourceMappings[0].State' \
      --output text)

    if [ "$provider_mapping_state" != "Enabled" ]; then
      echo "FAIL: Provider Lambda event source mapping is not enabled (state=$provider_mapping_state)"
      exit 1
    fi
    if [ "$result_mapping_state" != "Enabled" ]; then
      echo "FAIL: Result Lambda event source mapping is not enabled (state=$result_mapping_state)"
      exit 1
    fi
    if ! event_rule_exists "$OUTBOX_RULE_NAME"; then
      echo "FAIL: Outbox schedule rule is missing"
      exit 1
    fi

    echo "  OK: Lambda processing is active and ECS queue pollers are off"
  else
    if [ "$api_outbox_enabled" != "true" ]; then
      echo "FAIL: API outbox worker is not enabled for ECS mode (value=$api_outbox_enabled)"
      exit 1
    fi
    if [ "$api_result_enabled" != "true" ]; then
      echo "FAIL: API result queue worker is not enabled for ECS mode (value=$api_result_enabled)"
      exit 1
    fi
    if [ "$worker_desired_count" = "0" ]; then
      echo "FAIL: Provider ECS worker desired count is zero in ECS mode"
      exit 1
    fi
    if [ "$worker_running_count" = "0" ]; then
      echo "FAIL: Provider ECS worker has no running tasks in ECS mode"
      exit 1
    fi
    if lambda_function_exists "$PROVIDER_LAMBDA_NAME"; then
      echo "FAIL: Provider Lambda function still exists in ECS mode"
      exit 1
    fi
    if lambda_function_exists "$RESULT_LAMBDA_NAME"; then
      echo "FAIL: Result Lambda function still exists in ECS mode"
      exit 1
    fi
    if lambda_function_exists "$OUTBOX_LAMBDA_NAME"; then
      echo "FAIL: Outbox Lambda function still exists in ECS mode"
      exit 1
    fi
    if event_rule_exists "$OUTBOX_RULE_NAME"; then
      echo "FAIL: Outbox schedule rule still exists in ECS mode"
      exit 1
    fi

    echo "  OK: ECS processing is active and Lambda processing is off"
  fi

  if [ "$HTTP_EDGE_MODE" = "lambda" ]; then
    if [ "$api_desired_count" != "0" ]; then
      echo "FAIL: ECS control-plane-api desired count is not zero while HTTP edge mode is lambda (desired=$api_desired_count)"
      exit 1
    fi
    if [ "$api_running_count" != "0" ]; then
      echo "FAIL: ECS control-plane-api still has running tasks while HTTP edge mode is lambda (running=$api_running_count)"
      exit 1
    fi
    if ! lambda_function_exists "$CONTROL_PLANE_HTTP_LAMBDA_NAME"; then
      echo "FAIL: Control Plane HTTP Lambda function is missing"
      exit 1
    fi
    if ! http_api_exists; then
      echo "FAIL: Control Plane HTTP API Gateway edge is missing"
      exit 1
    fi

    echo "  OK: Lambda HTTP edge is active and ECS HTTP service is off"
    return
  fi

  if [ "$api_desired_count" = "0" ] || [ "$api_running_count" = "0" ]; then
    echo "FAIL: ECS control-plane-api service is not active for HTTP edge mode ecs (desired=$api_desired_count running=$api_running_count)"
    exit 1
  fi
  if lambda_function_exists "$CONTROL_PLANE_HTTP_LAMBDA_NAME"; then
    echo "FAIL: Control Plane HTTP Lambda function still exists while HTTP edge mode is ecs"
    exit 1
  fi
  if http_api_exists; then
    echo "FAIL: Control Plane HTTP API Gateway edge still exists while HTTP edge mode is ecs"
    exit 1
  fi

  echo "  OK: ECS HTTP edge is active and Lambda HTTP edge is off"
}

require_env_dir

echo "=== Processing mode switch ==="
echo "Environment: $ENV  Region: $REGION  Processing mode: $MODE  HTTP edge mode: $HTTP_EDGE_MODE"
echo ""

if [ "$VERIFY_ONLY" = true ]; then
  verify_mode
  if [ "$RUN_SMOKE_TEST" = true ]; then
    run_smoke_test_gate
  fi
  exit 0
fi

if { [ "$MODE" = "lambda" ] || [ "$HTTP_EDGE_MODE" = "lambda" ]; } && [ "$SKIP_PACKAGE" = false ]; then
  echo "Packaging Lambda artifacts..."
  "$REPO_ROOT/scripts/package-lambda-artifacts.sh"
  echo ""
fi

cd "$TF_DIR"

echo "terraform init"
terraform init
echo ""

PLAN_ARGS=(
  plan
  "-var-file=${ENV}.tfvars"
  "-var=enable_lambda_processing=${ENABLE_LAMBDA_PROCESSING}"
  "-var=enable_lambda_http_api=${ENABLE_LAMBDA_HTTP_API}"
  -out=tfplan
)

if [ "$MODE" = "lambda" ] || [ "$HTTP_EDGE_MODE" = "lambda" ]; then
  if [ -n "${LAMBDA_RUNTIME:-}" ]; then
    PLAN_ARGS+=("-var=lambda_runtime=${LAMBDA_RUNTIME}")
  fi
  if [ -n "${PROVIDER_LAMBDA_PACKAGE_PATH:-}" ]; then
    PLAN_ARGS+=("-var=provider_lambda_package_path=${PROVIDER_LAMBDA_PACKAGE_PATH}")
  fi
  if [ -n "${RESULT_LAMBDA_PACKAGE_PATH:-}" ]; then
    PLAN_ARGS+=("-var=result_lambda_package_path=${RESULT_LAMBDA_PACKAGE_PATH}")
  fi
  if [ -n "${OUTBOX_LAMBDA_PACKAGE_PATH:-}" ]; then
    PLAN_ARGS+=("-var=outbox_lambda_package_path=${OUTBOX_LAMBDA_PACKAGE_PATH}")
  fi
  if [ -n "${CONTROL_PLANE_HTTP_LAMBDA_PACKAGE_PATH:-}" ]; then
    PLAN_ARGS+=("-var=control_plane_http_lambda_package_path=${CONTROL_PLANE_HTTP_LAMBDA_PACKAGE_PATH}")
  fi
fi

echo "terraform plan"
terraform "${PLAN_ARGS[@]}"
echo ""

if [ "$PLAN_ONLY" = true ]; then
  echo "Plan-only mode. Exiting. Run without --plan-only to apply."
  exit 0
fi

echo "terraform apply"
terraform apply -auto-approve tfplan
echo ""

if [ "$SKIP_VERIFY" = true ]; then
  echo "Skipping verification (--skip-verify)"
  exit 0
fi

verify_mode

if [ "$RUN_SMOKE_TEST" = true ]; then
  run_smoke_test_gate
fi
