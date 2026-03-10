# Prompt Gateway - P2 Lifecycle and Timeout Plan

This plan addresses two P2 issues:
- `Started` lifecycle state exists but is not emitted in the current orchestration flow.
- `OpenAi.TimeoutSeconds` is validated but not enforced during provider calls.

---

## 1. Problem Statement

Current gaps:
- Job/attempt transitions move from `Dispatched` to `Completed`/`Failed` without a `Started` marker, reducing timeline fidelity.
- Long-running provider calls are not bounded by configured timeout, which can hold worker concurrency slots and delay throughput.

Impact:
- Weaker observability for in-flight execution.
- Harder latency analysis by stage.
- Risk of worker saturation under upstream slowness.

---

## 2. Goal

Improve execution visibility and operational safety by:
- Emitting `Started` state/event at the correct point in the flow.
- Enforcing per-request provider timeout from configuration.

---

## 3. Scope

In scope:
- Control Plane lifecycle/event updates for `Started`.
- Worker timeout enforcement for OpenAI calls.
- Tests and runbook updates for both behaviors.

Out of scope:
- Retry strategy redesign (covered in P3).
- Queue/infrastructure topology changes.

---

## 4. Work Plan

### P2-1 Lifecycle Transition Design
- Define exact moment for `Started` transition (recommended: when result ingestion receives first valid execution result signal, or introduce explicit worker start event).
- Ensure transition rules remain valid for retries and terminal states.

### P2-2 Control Plane State/Event Implementation
- Add `Started` state handling in orchestrator flow.
- Append `Started` job event with minimal attributes (`provider`, `model`, `attempt_id`).
- Keep terminal transitions unchanged (`Completed`/`Failed`).

### P2-3 Timeout Enforcement in Worker
- Wrap provider execution with a linked cancellation token that uses `OpenAi.TimeoutSeconds`.
- Map timeout to canonical error code (for example `provider_timeout`) for consistent ingestion behavior.
- Ensure visibility extension and message handling remain safe under timeout cancellation.

### P2-4 Telemetry
- Add structured logs for:
  - Transition to `Started`
  - Timeout-triggered cancellations
- Add counters/timers for timeout rate and started-to-terminal duration.

### P2-5 Test Coverage
- Add orchestrator tests asserting `Started` state/event behavior.
- Add worker tests verifying timeout cancellation and canonical timeout error publishing.
- Add regression tests to ensure successful jobs still complete and persist correctly.

### P2-6 Documentation
- Update lifecycle documentation to include real emitted `Started` semantics.
- Update operations runbook with timeout configuration behavior and troubleshooting.

---

## 5. Rollout Strategy

Phase A:
1. Deploy code with timeout enforcement and `Started` emission behind optional config flag (if desired).
2. Validate in dev with synthetic slow-provider scenarios.

Phase B:
1. Enable behavior in staging.
2. Verify event timelines and timeout metrics.

Phase C:
1. Enable in production.
2. Monitor for 24-72 hours and tune timeout defaults if needed.

---

## 6. Acceptance Criteria

- `Started` is observable in job event timeline for active executions.
- Timeout configuration is actually enforced at runtime.
- Timed-out provider calls produce canonical timeout errors and do not hang worker slots indefinitely.
- Existing success/failure and retry flows remain green in automated tests.

---

## 7. Tracking Checklist

- [ ] P2-1 Lifecycle transition point finalized
- [ ] P2-2 `Started` state/event implemented
- [ ] P2-3 Provider timeout enforcement implemented
- [ ] P2-4 Telemetry added
- [ ] P2-5 Tests added and passing
- [ ] P2-6 Docs/runbooks updated
- [ ] Dev validation complete
- [ ] Staging validation complete
- [ ] Production rollout complete

