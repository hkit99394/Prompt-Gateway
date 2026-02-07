Prompt Gateway – Control Plane (Orchestrator) Requirements

Role: Owns the contract, routing, lifecycle, audit, and client-visible state.

⸻

CP-01 Job intake & lifecycle
	•	Accept canonical job requests from Prompt Gateway API.
	•	Generate job_id and initial attempt_id.
	•	Persist job state and timestamps.
	•	Manage job state transitions:
	•	CREATED → ROUTED → DISPATCHED → STARTED → COMPLETED / FAILED
	•	Support RETRYING, CANCELLED, EXPIRED.

⸻

CP-02 Routing decision engine
	•	Evaluate routing policy per job:
	•	cost, latency, reliability, capability constraints.
	•	Select provider + model for each attempt.
	•	Persist routing decision with policy version and inputs.
	•	Produce fallback plan (ordered provider list).

⸻

CP-03 Dispatch management
	•	Publish job dispatch message to exactly one provider queue per attempt.
	•	Include:
	•	canonical request
	•	job_id, attempt_id, trace_id
	•	idempotency key
	•	Support re-dispatch on retry/fallback.

⸻

CP-04 Result ingestion
	•	Consume provider result events.
	•	Deduplicate results by (job_id, attempt_id).
	•	Update job and attempt state atomically.
	•	Decide whether to:
	•	finalize job
	•	retry
	•	fallback to another provider.

⸻

CP-05 Canonical response assembly
	•	Assemble final normalized response from provider output.
	•	Attach:
	•	provider identity + model
	•	normalized usage & cost
	•	canonical error (if failed)
	•	Persist final result reference.

⸻

CP-06 Client result access
	•	Expose APIs for:
	•	job status
	•	final result
	•	job event timeline
	•	Ensure consistent responses regardless of provider used.

⸻

CP-07 Audit & event timeline
	•	Append immutable job events:
	•	created, routed, dispatched, started, completed, failed, retried.
	•	Support GET /jobs/{job_id}/events.

⸻

CP-08 Idempotency & reliability
	•	Support at-least-once delivery from queues.
	•	Guarantee idempotent state transitions.
	•	Implement outbox pattern for dispatch events.

⸻

CP-09 Cost & usage normalization
	•	Normalize provider usage into canonical fields.
	•	Normalize billing into common schema:
	•	amount, currency, estimated flag.
	•	Persist per-attempt and final job cost.

⸻

CP-10 Observability
	•	Emit structured logs with job_id, attempt_id, provider.
	•	Emit metrics:
	•	latency, success rate, retries, cost.
	•	Propagate trace context.