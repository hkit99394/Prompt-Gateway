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
#   - BASE_URL: override (e.g. https://api.example.com); otherwise derived from ALB
#   - API_KEY: override; otherwise fetched from SSM (dev) or Secrets Manager (staging/prod)
#   - HEALTH_CHECK_BASE_URL: same as BASE_URL; used when set to avoid --insecure for custom domains
#   - AWS_REGION: default us-east-1
#
# Usage: ./scripts/first-deploy-phase4.sh [--insecure]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV="${ENV:-dev}"
REGION="${AWS_REGION:-us-east-1}"
USE_INSECURE=""
[ "${1:-}" = "--insecure" ] && USE_INSECURE="--insecure"

echo "=== First-deploy Phase 4: Smoke tests (T-5.4) ==="
echo "Environment: $ENV  Region: $REGION"
echo ""

# Resolve BASE_URL if not set
if [ -z "${BASE_URL:-}" ]; then
  BASE_URL="${HEALTH_CHECK_BASE_URL:-}"
fi
if [ -z "$BASE_URL" ]; then
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

# T-5.4.1 – T-5.4.5: run smoke-test.sh (GET /health, GET /ready, POST /jobs, poll, GET /result)
cd "$REPO_ROOT"
chmod +x scripts/smoke-test.sh
if [ -n "$USE_INSECURE" ]; then
  exec ./scripts/smoke-test.sh "$BASE_URL" "$API_KEY" "$USE_INSECURE"
else
  exec ./scripts/smoke-test.sh "$BASE_URL" "$API_KEY"
fi
