# Prompt Gateway – Implementation Backlog

This document turns the architecture review into a phase-by-phase implementation backlog with ticket IDs, suggested owners, dependencies, and acceptance criteria.

Suggested ownership follows the project-local agent roster in `.agents/README.md`:

- Use one primary owner per hotspot file.
- Add `release-verification` when a change affects rollout safety or runtime behavior.
- During the ECS-to-Lambda migration, prefer the Lambda-focused roster.

## Status Summary

| Phase | Goal | Outcome |
|------|------|---------|
| 0 | Normalize composition and deployment inputs | Complete: shared runtime composition roots are in place and ECS deployments use immutable image tags |
| 1 | Fix intake semantics | Complete: `POST /jobs` now has a durable acceptance boundary with idempotent replay and resumable post-accept processing |
| 2 | Harden provider execution | Reduce unnecessary retries and cost |
| 3 | Strengthen async event processing | Complete: outbox dequeue now uses indexed discovery and ECS/Lambda queue processing share the same unit-of-work contracts |
| 4 | Improve observability and rollout confidence | Detect migration issues earlier |
| 5 | Complete migration decisions and cleanup | Retire temporary paths safely |

---

## Phase 0 – Foundation

### PG-001 Shared control-plane composition root

- Status: Complete
- Primary owner: `runtime-extraction`
- Sidecar: `release-verification`
- Priority: High
- Dependencies: None

**Problem**

Control Plane API and result-processing Lambda bootstrap the same routing, retry, AWS, and orchestration services separately. This increases the chance of configuration drift.

**Scope**

- Extract shared Control Plane registration methods.
- Keep host-specific concerns thin.
- Preserve existing behavior.

**Target files**

- `Prompt Gateway – Control Plane /src/ControlPlane.Api/Program.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.ResultLambda/Function.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.OutboxLambda/Function.cs`
- new shared registration/extensions file(s) under `Prompt Gateway – Control Plane /src/`

**Acceptance criteria**

- API and Lambda entrypoints use the same shared registration path for:
  - routing options
  - retry options
  - AWS store/queue options
  - orchestrator/result-processing services
- Host files become thin shells.
- All existing Control Plane tests still pass.

**Completion notes**

- Shared runtime registration now lives in `ControlPlaneRuntimeServiceCollectionExtensions`.
- API, outbox Lambda, and result Lambda bootstrap through the same control-plane registration path.
- Control Plane test suite passes.

### PG-002 Shared provider composition root

- Status: Complete
- Primary owner: `runtime-extraction`
- Priority: High
- Dependencies: None

**Problem**

The ECS host and Lambda host for provider execution share most of the same service registration, but still bootstrap separately.

**Scope**

- Extract shared provider worker registrations.
- Keep Lambda-specific secret-loading separate and explicit.

**Target files**

- `Prompt Gateway Provider - OpenAI/src/Provider.Worker.Host/Program.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker.Lambda/Function.cs`
- new shared registration/extensions file(s) under `Prompt Gateway Provider - OpenAI/src/`

**Acceptance criteria**

- ECS host and Lambda host use the same service registration method.
- Lambda-specific secret hydration remains isolated to Lambda bootstrap.
- All existing provider tests still pass.

**Completion notes**

- Shared provider runtime registration now lives in `ProviderWorkerRuntimeServiceCollectionExtensions`.
- ECS host and Lambda host use the same provider registration path, while Lambda secret hydration stays in the Lambda bootstrap.
- Provider test suite passes.

### PG-003 Immutable deployment artifacts

- Status: Complete
- Primary owner: `lambda-platform`
- Sidecar: `release-verification`
- Priority: High
- Dependencies: None

**Problem**

ECS services currently point at mutable `:latest` images, which weakens reproducibility and rollback clarity.

**Scope**

- Replace mutable ECS image references with immutable tags or digests.
- Align deployment docs with immutable artifact promotion.

**Target files**

- `infra/terraform/modules/ecs-service/main.tf`
- `docs/DEPLOYMENT_PLAN.md`
- deployment scripts as needed

**Acceptance criteria**

- ECS task definitions no longer depend on mutable `:latest`.
- Rollback can be performed by selecting a known prior artifact.
- Deployment documentation matches the new artifact strategy.

**Completion notes**

- ECS task definitions now consume explicit `api_image_tag` and `worker_image_tag` inputs instead of `:latest`.
- Deploy script defaults to an immutable git-SHA-based tag.
- Deployment documentation and smoke/deploy flow were updated to match the immutable artifact strategy.

---

## Phase 1 – Intake Semantics

### PG-101 Intake idempotency contract

- Status: Complete
- Primary owner: `control-plane-core`
- Priority: High
- Dependencies: PG-001

**Problem**

Clients can hit ambiguous outcomes when `POST /jobs` partially succeeds and is retried.

**Scope**

- Add an idempotent submission contract.
- Support stable caller-provided identity for retries.
- Define behavior for duplicate submissions.

**Target files**

- `Prompt Gateway – Control Plane /src/ControlPlane.Api/Controllers/JobsController.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Core/JobOrchestrator.cs`
- Control Plane model/store abstractions as needed

**Acceptance criteria**

- Repeating the same intake request with the same idempotency input does not create duplicate jobs.
- The API returns the original durable handle for equivalent retries.
- Error responses clearly distinguish malformed input from duplicate/idempotent replay.

**Completion notes**

- `POST /jobs` supports `X-Idempotency-Key` and derives a stable job identity for equivalent retries.
- Equivalent retries return the original accepted handle, while conflicting payload reuse returns `409`.
- Intake comparison helpers were added to the canonical request model.

### PG-102 Durable acceptance boundary for `POST /jobs`

- Status: Complete
- Primary owner: `control-plane-core`
- Sidecar: `release-verification`
- Priority: High
- Dependencies: PG-001

**Problem**

`POST /jobs` currently accepts, routes, and dispatches inline. If accept succeeds and a later step fails, the client sees an error even though a job exists.

**Scope**

- Make durable acceptance the explicit success boundary.
- Move routing/dispatch to an asynchronous or resumable path after acceptance.
- Keep job lifecycle visibility intact.

**Target files**

- `Prompt Gateway – Control Plane /src/ControlPlane.Api/Controllers/JobsController.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Core/JobOrchestrator.cs`
- supporting processors or resume paths as needed

**Acceptance criteria**

- Successful API responses mean the job was durably accepted.
- Failures after acceptance do not require the client to guess whether the job exists.
- Retry and resume behavior is documented and tested.

**Completion notes**

- `POST /jobs` now returns after durable acceptance and no longer performs route/dispatch inline in the request path.
- Post-accept continuation is handled asynchronously by a dedicated resume worker, with manual `/jobs/{jobId}/resume` as the fallback path.
- API docs and smoke-test flow were updated for the `202 Accepted` contract and resume handling.

### PG-103 Intake and partial-failure test coverage

- Status: Complete
- Primary owner: `control-plane-core`
- Priority: High
- Dependencies: PG-101, PG-102

**Scope**

- Add tests for duplicate intake and partial failure behavior.

**Target files**

- `Prompt Gateway – Control Plane /tests/ControlPlane.Core.Tests/JobOrchestratorTests.cs`
- `Prompt Gateway – Control Plane /tests/ControlPlane.Core.Tests/ApiSecurityTests.cs`
- other Control Plane tests as needed

**Acceptance criteria**

- Tests cover:
  - duplicate intake with the same idempotency identity
  - accept succeeds but route fails
  - accept succeeds but dispatch fails
  - safe retry behavior after ambiguous client-side failure

**Completion notes**

- Control Plane API tests now cover duplicate intake, replay behavior, durable acceptance, no inline route/dispatch, and resume handling.
- Core orchestrator resume tests remain in place for created and terminal-state jobs.

---

## Phase 2 – Provider Execution Hardening

### PG-201 Provider error classification

- Status: Complete
- Primary owner: `provider-execution`
- Priority: High
- Dependencies: PG-002

**Problem**

Provider retries are currently too coarse and likely retry permanent failures.

**Scope**

- Classify transient vs terminal provider failures.
- Retry only transient conditions such as timeouts, throttling, and recoverable upstream failures.

**Target files**

- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Services/OpenAiClient.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Services/ProviderMessageProcessor.cs`
- provider models/errors as needed

**Acceptance criteria**

- Invalid auth, invalid request shape, and unsupported inputs fail fast.
- Transient failures continue to retry with backoff.
- Result publication and error reporting still behave correctly.

**Completion notes**

- `OpenAiClient` now classifies OpenAI failures before retrying, so authentication and invalid-request failures no longer consume retry attempts.
- Retryable transport and upstream conditions continue to use backoff, while terminal provider failures surface immediately as `OpenAiException`.
- Provider execution tests cover the classifier and terminal-error publication path.

### PG-202 Provider execution policy controls

- Primary owner: `provider-execution`
- Sidecar: `release-verification`
- Priority: Medium-High
- Dependencies: PG-201

**Problem**

Runtime knobs for concurrency, visibility timeout, OpenAI retry limits, and Lambda reserved concurrency need to work together predictably.

**Scope**

- Validate operational settings more explicitly.
- Document how ECS/Lambda concurrency and queue visibility should be tuned together.

**Target files**

- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Options/ProviderWorkerOptions.cs`
- `infra/terraform/modules/lambda-processing/main.tf`
- provider runtime docs as needed

**Acceptance criteria**

- Validation catches obviously unsafe or contradictory settings.
- Runtime docs define tuning rules for:
  - queue visibility timeout
  - worker concurrency
  - Lambda reserved concurrency
  - provider retry duration

### PG-203 Provider execution test expansion

- Status: Complete
- Primary owner: `provider-execution`
- Priority: Medium-High
- Dependencies: PG-201

**Scope**

- Add tests for retryable vs non-retryable provider errors.

**Target files**

- `Prompt Gateway Provider - OpenAI/tests/Provider.Worker.Tests/OpenAiClientTests.cs`
- `Prompt Gateway Provider - OpenAI/tests/Provider.Worker.Tests/ProviderMessageProcessorTests.cs`

**Acceptance criteria**

- Tests cover:
  - retryable upstream failures
  - non-retryable upstream failures
  - publish failure after successful provider call
  - terminal provider error publication

**Completion notes**

- `OpenAiClientTests` now cover retryable vs non-retryable OpenAI failure classification.
- `ProviderMessageProcessorTests` cover terminal provider error publication and publish-failure handling.
- Existing success-path publish-failure coverage remains in place.

---

## Phase 3 – Async Event Processing

### PG-301 Outbox dequeue redesign for scale

- Status: Complete
- Primary owner: `async-event-processing`
- Priority: High
- Dependencies: PG-001

**Problem**

The current outbox dequeue path depends on filtered queries over a shared partition, which is correct but may become inefficient under sustained load.

**Scope**

- Redesign how dispatchable outbox work is discovered.
- Preserve at-least-once dispatch and lease safety.

**Target files**

- `Prompt Gateway – Control Plane /src/ControlPlane.Aws/DynamoDbOutboxStore.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Core/DispatchOutboxProcessor.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Core/OutboxDispatchBatchProcessor.cs`
- DynamoDB infrastructure or schema docs as needed

**Acceptance criteria**

- Dequeue cost stays bounded under backlog.
- Work claiming remains safe under concurrency.
- Existing delivery guarantees are preserved.

**Completion notes**

- Outbox dequeue now uses the existing DynamoDB GSI to discover `OUTBOX_READY` and expired `OUTBOX_PROCESSING` work instead of filtering a shared `OUTBOX` partition.
- Outbox state transitions keep the GSI keys in sync across enqueue, claim, release, dispatch, and failure paths.
- Lease-recovery and indexed-query behavior are covered in `DynamoDbOutboxStoreTests`.

### PG-302 ECS and Lambda queue-processing parity

- Status: Complete
- Primary owner: `async-event-processing`
- Priority: Medium-High
- Dependencies: PG-001, PG-002

**Problem**

Queue work is already mostly shared, but ECS loops and Lambda handlers still create room for small behavioral drift.

**Scope**

- Align ack/retry semantics between ECS and Lambda paths.
- Make the unit-of-work contract explicit and reusable.

**Target files**

- `Prompt Gateway – Control Plane /src/ControlPlane.Api/OutboxWorker.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Api/ResultQueueWorker.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.OutboxLambda/Function.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.ResultLambda/Function.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker.Lambda/Function.cs`

**Acceptance criteria**

- ECS and Lambda use the same message-processing contract for success vs retry.
- Runtime-specific code only handles polling, batching, and trigger integration.
- No behavioral drift in duplicate or retry handling.

**Completion notes**

- The ECS outbox worker now uses `IOutboxDispatchBatchProcessor`, the same batch unit-of-work contract used by the outbox Lambda.
- Result queue and provider dispatch paths already share processor contracts, with ECS loops and Lambda handlers only handling polling or batch-failure integration.
- Async worker coverage now verifies the ECS outbox worker honors the configured batch limit.

### PG-303 Async processing tests for backlog and lease behavior

- Status: Complete
- Primary owner: `async-event-processing`
- Priority: Medium
- Dependencies: PG-301, PG-302

**Scope**

- Add tests for outbox lease recovery and batch processing semantics.

**Target files**

- `Prompt Gateway – Control Plane /tests/ControlPlane.Core.Tests/OutboxDispatchFunctionTests.cs`
- `Prompt Gateway – Control Plane /tests/ControlPlane.Core.Tests/ResultQueueFunctionTests.cs`
- `Prompt Gateway Provider - OpenAI/tests/Provider.Worker.Tests/ProviderDispatchFunctionTests.cs`

**Acceptance criteria**

- Tests cover:
  - lease expiry recovery
  - partial batch failure behavior
  - duplicate in-progress handling
  - batch limit behavior

**Completion notes**

- `DynamoDbOutboxStoreTests` cover lease-expiry recovery and indexed dequeue behavior.
- `OutboxDispatchFunctionTests`, `ResultQueueFunctionTests`, and `ProviderDispatchFunctionTests` cover batch-limit and partial-batch-failure semantics across Lambda handlers.
- Provider duplicate-in-progress handling remains covered through the shared processor and worker tests.

---

## Phase 4 – Observability And Rollout Confidence

### PG-401 Lambda and queue health alarms

- Primary owner: `lambda-platform`
- Sidecar: `observability`
- Priority: High
- Dependencies: PG-003

**Problem**

Monitoring currently favors ECS and generic DLQ visibility more than Lambda runtime health and primary queue pressure.

**Scope**

- Add Lambda-focused alarms and queue backlog alarms.

**Target files**

- `infra/terraform/modules/monitoring/main.tf`
- `infra/terraform/modules/lambda-processing/main.tf`

**Acceptance criteria**

- Monitoring covers:
  - Lambda errors
  - Lambda throttles
  - high duration / timeout pressure
  - primary queue age or visible backlog
  - reserved concurrency pressure where relevant

### PG-402 Readiness and smoke verification

- Primary owner: `release-verification`
- Priority: High
- Dependencies: PG-102, PG-401

**Problem**

`/ready` proves some dependencies are reachable, but rollout safety requires end-to-end runtime verification in the active mode.

**Scope**

- Keep readiness cheap.
- Expand smoke tests to verify real job flow.

**Target files**

- `Prompt Gateway – Control Plane /src/ControlPlane.Api/Health/AwsDependenciesHealthCheck.cs`
- `scripts/smoke-test.sh`
- `scripts/set-processing-mode.sh`

**Acceptance criteria**

- Readiness remains fast and operationally useful.
- Smoke verification covers:
  - job submission
  - dispatch
  - provider execution
  - result ingestion
  - successful completion in the active mode

### PG-403 Migration runbook hardening

- Primary owner: `lambda-platform`
- Sidecar: `release-verification`
- Priority: Medium-High
- Dependencies: PG-401, PG-402

**Scope**

- Update migration plans and cutover procedures to include explicit gates and rollback criteria.

**Target files**

- `docs/ECS_TO_LAMBDA_PLAN.md`
- `docs/DEPLOYMENT_PLAN.md`
- `scripts/set-processing-mode.sh`

**Acceptance criteria**

- Dev, staging, and prod each have:
  - cutover steps
  - rollback steps
  - verification gates
  - evidence expectations before promotion

---

## Phase 5 – Cleanup And Platform Decision

### PG-501 Retire ECS provider worker path

- Primary owner: `lambda-platform`
- Sidecar: `release-verification`
- Priority: Medium
- Dependencies: PG-201 through PG-403

**Scope**

- Remove ECS provider-worker runtime only after Lambda mode is proven through at least one full promotion cycle.

**Target files**

- `infra/terraform/modules/ecs-service/main.tf`
- `infra/terraform/README.md`
- rollout scripts and docs as needed

**Acceptance criteria**

- Lambda provider path is the only active provider execution runtime.
- Rollback evidence and migration confidence are documented before removal.

### PG-502 Final HTTP control-plane platform decision

- Primary owner: `control-plane-core`
- Sidecar: `lambda-platform`
- Priority: Medium
- Dependencies: PG-401 through PG-501

**Problem**

The HTTP API can remain on ECS or move to Lambda/API Gateway later, but that choice should be deliberate rather than accidental.

**Scope**

- Decide whether the API remains containerized or also moves serverless.
- Document operational tradeoffs.

**Target files**

- `docs/ECS_TO_LAMBDA_PLAN.md`
- `Prompt Gateway – Control Plane /src/ControlPlane.Api/Program.cs`
- infrastructure docs and modules as needed

**Acceptance criteria**

- A platform decision is documented.
- The repo and docs reflect that decision clearly.
- Any retained temporary architecture is called out explicitly.

---

## Suggested Execution Order

1. PG-001
2. PG-002
3. PG-003
4. PG-101
5. PG-102
6. PG-103
7. PG-201
8. PG-202
9. PG-203
10. PG-301
11. PG-302
12. PG-303
13. PG-401
14. PG-402
15. PG-403
16. PG-501
17. PG-502

## Recommended Milestone Gates

### Milestone A – Safe shared runtime wiring

Complete:

- PG-001
- PG-002
- PG-003

### Milestone B – Safe client intake

Complete:

- PG-101
- PG-102
- PG-103

### Milestone C – Safe provider behavior in Lambda mode

Complete:

- PG-201
- PG-202
- PG-203

### Milestone D – Safe async processing at scale

Complete:

- PG-301
- PG-302
- PG-303

### Milestone E – Promotion-ready migration

Complete:

- PG-401
- PG-402
- PG-403

### Milestone F – Post-migration cleanup

Complete:

- PG-501
- PG-502
