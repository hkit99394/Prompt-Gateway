# Prompt Gateway – P1 Contract Fix Plan

This plan addresses P1: the dispatch contract mismatch between Control Plane and Provider Worker that can cause prompt-load failures at runtime.

---

## 1. Problem Statement

Current risk:
- Control Plane dispatch payload schema does not align with Worker prompt-loading expectations.
- Worker expects prompt reference fields (`prompt_key` / `prompt_s3_key`) while Control Plane canonical request currently centers around `input_ref`.
- Result: jobs can be accepted and dispatched but fail during worker prompt retrieval.

Impact:
- Runtime failures after job acceptance.
- Increased retries, failed jobs, and DLQ risk.
- Contract drift across services.

---

## 2. Goal

Define and enforce one canonical dispatch contract shared by both services, with backward compatibility during rollout and safe deprecation of legacy fields.

---

## 3. Scope

In scope:
- Shared request contract definition for dispatch messages.
- Control Plane mapping/validation before enqueue.
- Worker compatibility for new and legacy fields.
- Contract tests and end-to-end dispatch/consume coverage.
- Rollout sequencing and observability checks.

Out of scope:
- New routing algorithms.
- Non-OpenAI provider protocol design.
- Infra redesign (SQS/DynamoDB topology unchanged).

---

## 4. Canonical Contract (Target)

Required fields:
- `job_id`
- `attempt_id`
- `trace_id`
- `task_type`

Prompt reference fields (at least one source required):
- `prompt_key` and optional `prompt_bucket`
- `prompt_s3_key` and optional `prompt_s3_bucket`

Optional fields:
- `model`
- `system_prompt`
- `metadata`
- `prompt_variables`
- `prompt_input`
- `parameters`
- `contract_version`

Rule:
- Producer must emit at least one valid prompt reference path unless task type explicitly does not require prompt templates.

---

## 5. Work Plan

### P1-1 Shared Contract Source
- Create a shared contracts project/package used by Control Plane and Worker.
- Move dispatch request DTOs to shared code.
- Remove duplicate contract definitions where possible.

### P1-2 Control Plane Producer Hardening
- Add mapping from legacy inputs to canonical prompt fields.
- Validate prompt reference availability at `POST /jobs` time.
- Return `400` for invalid/missing prompt source (fail fast).
- Ensure dispatch serialization always emits target contract format.

### P1-3 Worker Consumer Compatibility
- Prioritize reading new canonical fields.
- Keep temporary fallback support for legacy field names.
- Emit structured warning when legacy payload path is used.

### P1-4 Contract Versioning
- Add `contract_version` to dispatched payloads.
- Log contract version in producer and consumer.
- Define deprecation cutoff for legacy parsing.

### P1-5 Test Coverage
- Add producer serialization contract tests.
- Add consumer deserialization compatibility tests.
- Add integration test:
  - Create job -> enqueue dispatch -> worker parses -> prompt load path succeeds.
- Add negative tests for missing prompt references (expect `400` at intake).

### P1-6 Documentation Updates
- Update API/worker docs with exact canonical fields.
- Add migration note for old payload producers.
- Reference this plan from deployment/runbook docs.

---

## 6. Rollout Strategy

Phase A (Compatibility first):
1. Deploy Worker with dual-read support (new + legacy).
2. Verify no regression in current traffic.

Phase B (Producer switch):
1. Deploy Control Plane writing canonical contract + `contract_version`.
2. Monitor parse failures, prompt-load failures, and DLQ.

Phase C (Cleanup):
1. After stability window, remove legacy parsing path in Worker.
2. Keep versioned contract checks and tests.

---

## 7. Acceptance Criteria

- Zero prompt-load failures caused by missing/incorrect dispatch contract fields in staging validation window.
- `POST /jobs` rejects invalid prompt reference payloads with `400`.
- Contract tests pass in both service pipelines.
- End-to-end test validates dispatch-to-consume happy path.
- No increase in SQS DLQ count attributable to contract parse/load errors after rollout.

---

## 8. Tracking Checklist

- [ ] P1-1 Shared contract project introduced
- [ ] P1-2 Control Plane mapping + validation implemented
- [ ] P1-3 Worker dual-read compatibility implemented
- [ ] P1-4 `contract_version` emitted and logged
- [ ] P1-5 Contract + integration tests added
- [ ] P1-6 Docs updated
- [ ] Phase A deployed
- [ ] Phase B deployed
- [ ] Phase C cleanup complete

