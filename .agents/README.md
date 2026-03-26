# Prompt Gateway Agent Roster

This folder defines the working subagents for the ECS-to-Lambda migration and the surrounding runtime work.

## How to use this roster

1. Pick one primary owner for the main code path you are changing.
2. Add `release-verification` when the change affects runtime behavior, rollout safety, or migration confidence.
3. Avoid assigning multiple agents to the same hotspot file.
4. For small changes, one primary owner is enough.
5. For larger changes, use one domain owner plus one sidecar verifier.

## Migration roster

- `runtime-extraction`
  - Refactors long-running workers into host-agnostic handlers.
- `control-plane-core`
  - Owns orchestration, lifecycle, retry, and domain rules.
- `provider-execution`
  - Owns OpenAI execution behavior and provider-facing logic.
- `async-event-processing`
  - Owns queue-triggered runtime behavior, batch handling, and dedupe semantics.
- `lambda-platform`
  - Owns Lambda infrastructure, IAM, event source mappings, and deployment wiring.
- `release-verification`
  - Owns migration safety checks, smoke tests, and rollout verification.

## Sidecar specialists

- `code-review`
  - Reviews implementation changes for regressions, missing tests, and contract drift.
- `aws-security`
  - Reviews IAM, secret handling, encryption, exposure, and AWS runtime security posture.
- `observability`
  - Reviews logs, metrics, alarms, traceability, DLQ visibility, and migration telemetry.

## Suggested phase mapping

- Phase 0: `lambda-platform`, `control-plane-core`
- Phase 1: `runtime-extraction`, `control-plane-core`, `provider-execution`
- Phase 2: `async-event-processing`, `lambda-platform`, `release-verification`
- Phase 3: `lambda-platform`, `release-verification`
- Phase 4: `control-plane-core`, `lambda-platform`
- Phase 5: `lambda-platform`, `release-verification`

## Single-owner hotspots

- `Prompt Gateway – Control Plane /src/ControlPlane.Core/JobOrchestrator.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs`
- `infra/terraform/modules/iam/main.tf`
- `infra/terraform/modules/ecs-service/main.tf` until it is retired

## Files

- `runtime-extraction.md`
- `control-plane-core.md`
- `provider-execution.md`
- `async-event-processing.md`
- `lambda-platform.md`
- `release-verification.md`
- `code-review.md`
- `aws-security.md`
- `observability.md`
