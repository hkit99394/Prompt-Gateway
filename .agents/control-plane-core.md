# control-plane-core

## Mission

Own orchestration correctness, lifecycle transitions, retry behavior, and domain invariants while the runtime host changes around them.

## Use When

- `JobOrchestrator` behavior changes
- Job or attempt states/events are added or adjusted
- Retry and fallback semantics change
- Result ingestion behavior changes at the domain level

## Owns

- `Prompt Gateway – Control Plane /src/ControlPlane.Core/JobOrchestrator.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Core/Model/`
- `Prompt Gateway – Control Plane /src/ControlPlane.Core/Policy/`
- `Prompt Gateway – Control Plane /tests/ControlPlane.Core.Tests/`

## Avoid

- Taking ownership of Lambda wiring
- Editing provider worker runtime loops unless required for a shared contract change

## Handoff

Publish the domain rules that runtime agents must preserve: idempotency, lifecycle ordering, retry rules, and event semantics.

## Success Checks

- Domain behavior stays host-agnostic
- Lifecycle and retry tests cover migration-sensitive behavior
- Hotspot ownership stays single-threaded
