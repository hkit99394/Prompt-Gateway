# Prompt Gateway - P3 Retry Policy Hardening Plan

This plan addresses P3: provider retry logic is currently too broad and retries generic exceptions.

---

## 1. Problem Statement

Current behavior retries on broad exception classes, which can include non-transient failures (for example invalid request payloads or deterministic provider validation errors).

Impact:
- Avoidable latency per failed job.
- Unnecessary upstream load and cost.
- Noisy failure patterns that hide true transient incidents.

---

## 2. Goal

Restrict retries to clearly transient failure categories and fail fast on deterministic errors, while preserving resilience for throttling and short-lived outages.

---

## 3. Scope

In scope:
- Retry decision classification for provider call failures.
- Retry backoff tuning and observability.
- Tests that assert retry/non-retry behavior by error class.

Out of scope:
- Control Plane fallback policy logic.
- Multi-provider policy optimization.

---

## 4. Retry Policy Target

Retryable (examples):
- HTTP 429
- HTTP 408
- HTTP 5xx (transient service-side failures)
- Network transport/transient connectivity failures

Non-retryable (examples):
- 400/401/403/404 semantic or auth errors
- Invalid model/parameter constraints
- Malformed request payloads

Policy shape:
- Max attempts from config.
- Exponential backoff with jitter.
- Immediate fail for non-retryable errors.

---

## 5. Work Plan

### P3-1 Error Classification Layer
- Introduce a central classifier to map provider exceptions into retryable vs non-retryable categories.
- Include provider error code and HTTP status when available.

### P3-2 OpenAI Client Retry Update
- Replace generic catch/retry with classifier-driven retry checks.
- Preserve cancellation semantics and configured max attempts.
- Ensure timeout-induced cancellations are not retried unless explicitly classified.

### P3-3 Canonical Error Mapping
- Standardize error codes so non-retryable causes are visible (`invalid_request`, `auth_error`, `provider_error`, `provider_timeout`, etc.).
- Attach provider code/status where possible for diagnostics.

### P3-4 Telemetry
- Log retry decision reason per attempt.
- Add metrics:
  - retry_attempt_count
  - retry_exhausted_count
  - non_retryable_failure_count

### P3-5 Test Coverage
- Unit tests for classifier by status/error-code matrix.
- Client tests that verify:
  - transient errors retry
  - deterministic errors do not retry
  - backoff attempts stop at configured maximum

### P3-6 Documentation
- Document retry matrix in worker runbook.
- Document recommended defaults and tuning guidance.

---

## 6. Rollout Strategy

Phase A:
1. Deploy classifier + metrics with conservative defaults.
2. Validate transient retry behavior in dev/staging failure-injection tests.

Phase B:
1. Enable in production.
2. Monitor retry rates, latency, and non-retryable error profile.

Phase C:
1. Tune retryable matrix and backoff settings based on production data.

---

## 7. Acceptance Criteria

- Non-transient provider errors are no longer retried.
- Transient errors still retry and recover where possible.
- Retry metrics and decision logs are visible and actionable.
- No regression in successful throughput under normal load.

---

## 8. Tracking Checklist

- [ ] P3-1 Error classifier implemented
- [ ] P3-2 Client retry behavior updated
- [ ] P3-3 Canonical error mapping aligned
- [ ] P3-4 Retry telemetry added
- [ ] P3-5 Tests added and passing
- [ ] P3-6 Docs updated
- [ ] Staging validation complete
- [ ] Production rollout complete

