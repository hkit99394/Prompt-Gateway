# ControlPlane.Core

This folder contains the cloud-agnostic domain model and orchestration logic for the
Control Plane. Infrastructure and policy decisions are expressed as interfaces so
other projects (AWS or other clouds) can implement them without changing core logic.

## Abstractions.cs

### IClock
Provides UTC time used for consistent timestamps in orchestration.

### IIdGenerator
Creates deterministic identifiers for jobs, attempts, and trace IDs.

### IJobStore
Persists and retrieves the `JobRecord` aggregate.

### IJobEventStore
Appends immutable `JobEvent` entries and returns the job timeline.

### IOutboxStore
Implements the outbox pattern for dispatch messages.

### IDeduplicationStore
Ensures idempotent result ingestion per (job_id, attempt_id).

### IRoutingPolicy
Selects provider and model per job request.

### IDispatchQueue
Publishes `DispatchMessage` to a provider queue.

### IResultStore
Persists per-attempt and final `CanonicalResponse` payloads.

### ICanonicalResponseAssembler
Normalizes provider results into a `CanonicalResponse`.

### IRetryPlanner
Determines whether to retry and which provider/model to use.

## Models.cs

### JobState
Job lifecycle: Created, Routed, Dispatched, Started, Completed, Failed,
Retrying, Cancelled, Expired.

### AttemptState
Attempt lifecycle: Created, Routed, Dispatched, Started, Completed, Failed.

### JobEventType
Event timeline markers for audit and client-facing history.

### ResultIngestionStatus
Result ingestion outcome: Duplicate, JobNotFound, Finalized, Retrying.

### UsageMetrics
Normalized token counts (prompt, completion, total).

### CostMetrics
Normalized billing fields (amount, currency, estimated flag).

### CanonicalError
Normalized error fields with optional provider error code.

### CanonicalJobRequest
Canonical job input plus optional IDs and metadata. `WithIds` creates
an immutable copy with generated IDs.

### JobHandle
Return type for intake: job ID, attempt ID, trace ID.

### RoutingDecision
Policy output: provider, model, policy version, and fallback providers.

### DispatchMessage
Queue message sent to providers. Includes idempotency key and full request.

### ProviderResultEvent
Result message returned by provider worker.

### CanonicalResponse
Normalized response returned to clients.

### JobEvent
Immutable audit entry with helper factories for common event types.

### JobAttempt
Attempt state, routing decision, and timestamps.

### JobRecord
Aggregate for a job, containing attempts and current state.
Includes `Create` for new jobs and `Restore` for storage snapshots.

### RetryPlan
Decision from `IRetryPlanner`, with convenience factories.

### OutboxDispatchMessage
Outbox record containing a dispatch message and creation timestamp.

### ResultIngestionOutcome
Result of `IngestResultAsync`: duplicate, finalized, or retrying.

### JobAttemptSnapshot
Serializable snapshot for a single attempt.

### JobRecordSnapshot
Serializable snapshot for a job and its attempts.

## Policies.cs

### RoutingPolicyOptions
Configuration for static routing (provider, model, policy version, fallbacks).

### StaticRoutingPolicy
Returns a routing decision from `RoutingPolicyOptions`.

### RetryPlannerOptions
Configuration for retry behavior (max attempts).

### FallbackRetryPlanner
Retries using remaining fallback providers until the max attempt count is reached.

### SimpleResponseAssembler
Maps a provider result into a canonical response, adding a default error if needed.

## JobOrchestrator.cs

### JobOrchestrator
Primary orchestration flow:
- Accepts jobs and emits Created events.
- Routes attempts using `IRoutingPolicy`.
- Dispatches by writing to the outbox.
- Ingests results with dedupe, finalization, or retry logic.
- Exposes read access for job status, final result, and events.

## DispatchOutboxProcessor.cs

### DispatchOutboxProcessor
Processes one outbox record:
- Dequeues the next pending dispatch.
- Publishes to `IDispatchQueue`.
- Marks it as dispatched.

## SystemDefaults.cs

### SystemClock
Default UTC clock implementation.

### GuidIdGenerator
Creates GUID-based IDs for jobs, attempts, and traces.
