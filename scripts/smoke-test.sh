#!/usr/bin/env bash
# Smoke test script for Prompt Gateway Control Plane API
# Implementation: T-7.1 – T-7.8
#
# Usage: ./scripts/smoke-test.sh BASE_URL API_KEY [--insecure]
#
# Args:
#   BASE_URL  - Base URL (e.g. http://alb-dns or https://api.example.com)
#   API_KEY   - X-API-Key value for authentication
#   --insecure - Optional: skip SSL cert verification (curl -k) for HTTPS URLs
 # Env:
#   SMOKE_INPUT_REF - Optional prompt reference key (default: prompts/smoke-test.txt)
#
# Exits 0 on success, 1 on failure.

set -euo pipefail

if [ $# -lt 2 ]; then
  echo "Usage: $0 BASE_URL API_KEY [--insecure]"
  exit 1
fi

BASE_URL="${1%/}"
API_KEY="$2"
INPUT_REF="${SMOKE_INPUT_REF:-prompts/smoke-test.txt}"
CURL_OPTS=(-sf)
if [ "${3:-}" = "--insecure" ]; then
  CURL_OPTS=(-skf)
fi

echo "Smoke test: BASE_URL=$BASE_URL"

# T-7.3: GET /health
echo "  GET /health..."
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "${CURL_OPTS[@]}" "$BASE_URL/health" || true)
if [ "$HTTP_CODE" != "200" ]; then
  echo "  FAIL: /health returned $HTTP_CODE (expected 200)"
  exit 1
fi
echo "  OK: /health -> 200"

# T-7.4: GET /ready (includes AWS dependency check: DynamoDB, SQS)
echo "  GET /ready..."
READY_RESPONSE=$(curl -s -w "\n%{http_code}" "${CURL_OPTS[@]}" -H "X-API-Key: $API_KEY" "$BASE_URL/ready" || true)
READY_BODY=$(echo "$READY_RESPONSE" | sed '$d')
HTTP_CODE=$(echo "$READY_RESPONSE" | tail -n 1)
if [ "$HTTP_CODE" != "200" ]; then
  echo "  FAIL: /ready returned $HTTP_CODE (expected 200)"
  if [ -n "$READY_BODY" ]; then
    echo "  Response body (reason): $READY_BODY"
  fi
  echo "  Tip: /ready fails if AwsStorage__TableName, AwsQueue__DispatchQueueUrl, or AwsQueue__ResultQueueUrl are missing or unreachable. Check ECS task definition env and IAM."
  exit 1
fi
echo "  OK: /ready -> 200"

# T-7.5: POST /jobs with minimal valid payload
# Use curl without -f so we capture body and HTTP code on 4xx/5xx for diagnostics
CURL_OPTS_NOFAIL=(-s)
[ "${3:-}" = "--insecure" ] && CURL_OPTS_NOFAIL=(-sk)
echo "  POST /jobs..."
RESPONSE=$(curl -w "\n%{http_code}" "${CURL_OPTS_NOFAIL[@]}" \
  -X POST \
  -H "Content-Type: application/json" \
  -H "X-API-Key: $API_KEY" \
  -d "{\"taskType\":\"chat_completion\",\"inputRef\":\"$INPUT_REF\"}" \
  "$BASE_URL/jobs")
BODY=$(echo "$RESPONSE" | sed '$d')
HTTP_CODE=$(echo "$RESPONSE" | tail -n 1)

# Accept 200 (Ok) or 201 (Created) per REST conventions for resource creation
if [ "$HTTP_CODE" != "200" ] && [ "$HTTP_CODE" != "201" ]; then
  echo "  FAIL: POST /jobs returned $HTTP_CODE (expected 200 or 201)"
  echo "  Response: $BODY"
  exit 1
fi

JOB_ID=$(echo "$BODY" | jq -r '.jobId // .JobId // empty')
if [ -z "$JOB_ID" ]; then
  echo "  FAIL: POST /jobs response missing jobId"
  echo "  Response: $BODY"
  exit 1
fi
echo "  OK: POST /jobs -> $HTTP_CODE, jobId=$JOB_ID"

# T-7.6: Poll GET /jobs/{job_id} until Completed or Failed (timeout 60s)
echo "  Polling GET /jobs/$JOB_ID..."
TIMEOUT=60
ELAPSED=0
INTERVAL=3
STATE=""

while [ $ELAPSED -lt $TIMEOUT ]; do
  JOB_RESPONSE=$(curl -s "${CURL_OPTS[@]}" -H "X-API-Key: $API_KEY" "$BASE_URL/jobs/$JOB_ID" || echo "{}")
  STATE=$(echo "$JOB_RESPONSE" | jq -r '.State // .state // empty' 2>/dev/null || echo "")

  # Accept string ("Completed"/"Failed") or integer (4=Completed, 5=Failed) per JobState enum
  if [ "$STATE" = "Completed" ] || [ "$STATE" = "4" ]; then
    echo "  OK: Job completed"
    break
  fi
  if [ "$STATE" = "Failed" ] || [ "$STATE" = "5" ]; then
    echo "  FAIL: Job failed (T-7.8)"
    echo "  Response: $JOB_RESPONSE"
    exit 1
  fi

  sleep $INTERVAL
  ELAPSED=$((ELAPSED + INTERVAL))
  echo "    ... state=$STATE (${ELAPSED}s)"
done

if [ "$STATE" != "Completed" ] && [ "$STATE" != "4" ]; then
  echo "  FAIL: Timeout after ${TIMEOUT}s, state=$STATE"
  exit 1
fi

# T-7.7: GET /jobs/{job_id}/result
echo "  GET /jobs/$JOB_ID/result..."
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "${CURL_OPTS[@]}" -H "X-API-Key: $API_KEY" "$BASE_URL/jobs/$JOB_ID/result" || true)
if [ "$HTTP_CODE" != "200" ]; then
  echo "  FAIL: GET /jobs/$JOB_ID/result returned $HTTP_CODE (expected 200)"
  exit 1
fi
echo "  OK: GET /jobs/$JOB_ID/result -> 200"

echo "Smoke test passed."
