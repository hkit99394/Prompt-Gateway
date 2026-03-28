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
#   SMOKE_INLINE_PROMPT_TEXT - Optional inline prompt body for the per-request path
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
INLINE_PROMPT_TEXT="${SMOKE_INLINE_PROMPT_TEXT:-Reply with exactly: inline smoke test ok}"
CURL_OPTS=(-sf)
if [ "${3:-}" = "--insecure" ]; then
  CURL_OPTS=(-skf)
fi

echo "Smoke test: BASE_URL=$BASE_URL"
echo "Smoke test: INPUT_REF=$INPUT_REF"
echo "Smoke test: INLINE_PROMPT_TEXT configured"

print_job_diagnostics() {
  local job_id="$1"

  if [ -z "$job_id" ]; then
    return
  fi

  echo "  Diagnostic: GET /jobs/$job_id"
  curl -s "${CURL_OPTS[@]}" -H "X-API-Key: $API_KEY" "$BASE_URL/jobs/$job_id" || true
  echo ""

  echo "  Diagnostic: GET /jobs/$job_id/events"
  curl -s "${CURL_OPTS[@]}" -H "X-API-Key: $API_KEY" "$BASE_URL/jobs/$job_id/events" || true
  echo ""
}

run_job_flow() {
  local scenario="$1"
  local post_body="$2"
  local response
  local body
  local http_code
  local job_id
  local requires_resume
  local resume_response
  local resume_body
  local resume_http_code
  local timeout
  local elapsed
  local interval
  local state
  local job_response

  echo "  Scenario: $scenario"
  echo "  POST /jobs..."
  response=$(curl -w "\n%{http_code}" "${CURL_OPTS_NOFAIL[@]}" \
    -X POST \
    -H "Content-Type: application/json" \
    -H "X-API-Key: $API_KEY" \
    -d "$post_body" \
    "$BASE_URL/jobs")
  body=$(echo "$response" | sed '$d')
  http_code=$(echo "$response" | tail -n 1)

  if [ "$http_code" != "200" ] && [ "$http_code" != "201" ] && [ "$http_code" != "202" ]; then
    echo "  FAIL: POST /jobs returned $http_code (expected 200, 201, or 202)"
    echo "  Response: $body"
    exit 1
  fi

  job_id=$(echo "$body" | jq -r '.jobId // .JobId // empty')
  if [ -z "$job_id" ]; then
    echo "  FAIL: POST /jobs response missing jobId"
    echo "  Response: $body"
    exit 1
  fi
  echo "  OK: POST /jobs -> $http_code, jobId=$job_id"

  requires_resume=$(echo "$body" | jq -r '.requiresResume // .RequiresResume // false' 2>/dev/null || echo "false")
  if [ "$requires_resume" = "true" ]; then
    echo "  Job requires resume. POST /jobs/$job_id/resume..."
    resume_response=$(curl -w "\n%{http_code}" "${CURL_OPTS_NOFAIL[@]}" \
      -X POST \
      -H "X-API-Key: $API_KEY" \
      "$BASE_URL/jobs/$job_id/resume")
    resume_body=$(echo "$resume_response" | sed '$d')
    resume_http_code=$(echo "$resume_response" | tail -n 1)

    if [ "$resume_http_code" != "200" ] && [ "$resume_http_code" != "202" ]; then
      echo "  FAIL: POST /jobs/$job_id/resume returned $resume_http_code (expected 200 or 202)"
      echo "  Response: $resume_body"
      print_job_diagnostics "$job_id"
      exit 1
    fi

    echo "  OK: POST /jobs/$job_id/resume -> $resume_http_code"
  fi

  echo "  Polling GET /jobs/$job_id..."
  timeout="${SMOKE_TIMEOUT_SECONDS:-90}"
  elapsed=0
  interval=3
  state=""

  while [ $elapsed -lt $timeout ]; do
    job_response=$(curl -s "${CURL_OPTS[@]}" -H "X-API-Key: $API_KEY" "$BASE_URL/jobs/$job_id" || echo "{}")
    state=$(echo "$job_response" | jq -r '.State // .state // empty' 2>/dev/null || echo "")

    if [ "$state" = "Completed" ] || [ "$state" = "4" ]; then
      echo "  OK: Job completed"
      break
    fi
    if [ "$state" = "Failed" ] || [ "$state" = "5" ]; then
      echo "  FAIL: Job failed (T-7.8)"
      echo "  Response: $job_response"
      print_job_diagnostics "$job_id"
      exit 1
    fi

    sleep $interval
    elapsed=$((elapsed + interval))
    echo "    ... state=$state (${elapsed}s)"
  done

  if [ "$state" != "Completed" ] && [ "$state" != "4" ]; then
    job_response=$(curl -s "${CURL_OPTS[@]}" -H "X-API-Key: $API_KEY" "$BASE_URL/jobs/$job_id" || echo "{}")
    state=$(echo "$job_response" | jq -r '.State // .state // empty' 2>/dev/null || echo "")

    if [ "$state" = "Completed" ] || [ "$state" = "4" ]; then
      echo "  OK: Job completed"
    else
      echo "  FAIL: Timeout after ${timeout}s, state=$state"
      print_job_diagnostics "$job_id"
      exit 1
    fi
  fi

  echo "  GET /jobs/$job_id/result..."
  http_code=$(curl -s -o /dev/null -w "%{http_code}" "${CURL_OPTS[@]}" -H "X-API-Key: $API_KEY" "$BASE_URL/jobs/$job_id/result" || true)
  if [ "$http_code" != "200" ]; then
    echo "  FAIL: GET /jobs/$job_id/result returned $http_code (expected 200)"
    print_job_diagnostics "$job_id"
    exit 1
  fi
  echo "  OK: GET /jobs/$job_id/result -> 200"
}

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
REF_POST_BODY=$(jq -nc --arg inputRef "$INPUT_REF" '{taskType:"chat_completion",inputRef:$inputRef}')
INLINE_POST_BODY=$(jq -nc --arg promptText "$INLINE_PROMPT_TEXT" '{taskType:"chat_completion",promptText:$promptText}')

run_job_flow "prepared prompt reference" "$REF_POST_BODY"
run_job_flow "inline prompt request" "$INLINE_POST_BODY"

echo "Smoke test passed."
