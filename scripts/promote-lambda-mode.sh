#!/usr/bin/env bash
# Promote Lambda processing mode to staging or prod while keeping ECS mode available
# as a supported fallback. This script deploys Lambda mode for the target environment,
# verifies the mode switch, and runs the smoke test.
#
# Optional env vars:
#   - AWS_REGION: default us-east-1
#   - HTTP_EDGE_MODE: lambda (default) or ecs
#   - HEALTH_CHECK_BASE_URL / BASE_URL: forwarded to first-deploy-phase4.sh
#   - API_KEY: forwarded to first-deploy-phase4.sh
#   - SMOKE_INPUT_REF / SMOKE_PROMPT_BUCKET / SMOKE_PROMPT_TEXT / SMOKE_SKIP_PROMPT_UPLOAD
#   - LAMBDA_RUNTIME / PROVIDER_LAMBDA_PACKAGE_PATH / RESULT_LAMBDA_PACKAGE_PATH / OUTBOX_LAMBDA_PACKAGE_PATH / CONTROL_PLANE_HTTP_LAMBDA_PACKAGE_PATH
#
# Usage:
#   ./scripts/promote-lambda-mode.sh staging
#   ./scripts/promote-lambda-mode.sh prod
#   ./scripts/promote-lambda-mode.sh prod --skip-smoke
#   ./scripts/promote-lambda-mode.sh prod --skip-staging-check

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REGION="${AWS_REGION:-us-east-1}"
HTTP_EDGE_MODE="${HTTP_EDGE_MODE:-lambda}"
TARGET_ENV="${1:-}"
CHECK_STAGING=false
STAGING_CHECK_EXPLICIT=false
SKIP_SMOKE=false
SKIP_VERIFY=false

shift $(( $# > 0 ? 1 : 0 ))

while [ $# -gt 0 ]; do
  case "$1" in
    --check-staging)
      CHECK_STAGING=true
      STAGING_CHECK_EXPLICIT=true
      shift
      ;;
    --skip-staging-check)
      CHECK_STAGING=false
      STAGING_CHECK_EXPLICIT=true
      shift
      ;;
    --skip-smoke)
      SKIP_SMOKE=true
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

if [ "$TARGET_ENV" != "staging" ] && [ "$TARGET_ENV" != "prod" ]; then
  echo "Usage: ./scripts/promote-lambda-mode.sh <staging|prod> [--check-staging|--skip-staging-check] [--skip-smoke] [--skip-verify]"
  exit 1
fi

if [ "$TARGET_ENV" = "prod" ] && [ "$STAGING_CHECK_EXPLICIT" = false ]; then
  CHECK_STAGING=true
fi

if [ "$TARGET_ENV" = "prod" ] && [ "$CHECK_STAGING" = true ]; then
  echo "Checking staging Lambda mode before prod promotion..."
  ENV=staging AWS_REGION="$REGION" HTTP_EDGE_MODE="$HTTP_EDGE_MODE" "$REPO_ROOT/scripts/set-processing-mode.sh" --mode lambda --verify-only
  echo ""
fi

echo "=== Promote Lambda Mode ==="
echo "Target environment: $TARGET_ENV  Region: $REGION  HTTP edge mode: $HTTP_EDGE_MODE"
echo ""

ENV="$TARGET_ENV" AWS_REGION="$REGION" HTTP_EDGE_MODE="$HTTP_EDGE_MODE" "$REPO_ROOT/scripts/first-deploy-phase3.sh" --processing-mode lambda
echo ""

if [ "$SKIP_VERIFY" = false ]; then
  ENV="$TARGET_ENV" AWS_REGION="$REGION" HTTP_EDGE_MODE="$HTTP_EDGE_MODE" "$REPO_ROOT/scripts/set-processing-mode.sh" --mode lambda --verify-only
  echo ""
fi

if [ "$SKIP_SMOKE" = false ]; then
  ENV="$TARGET_ENV" AWS_REGION="$REGION" HTTP_EDGE_MODE="$HTTP_EDGE_MODE" "$REPO_ROOT/scripts/first-deploy-phase4.sh"
  echo ""
fi

echo "Lambda mode promotion complete for $TARGET_ENV."
