Prompt Gateway – Provider Worker Requirements

Role: Provider-specific adapter that executes jobs and normalizes output.

⸻

PW-01 Queue subscription
	•	Subscribe to exactly one provider-specific queue.
	•	Support parallel job processing with controlled concurrency.

⸻

PW-02 Canonical request handling
	•	Accept canonical job request.
	•	Validate task type and supported features.
	•	Reject unsupported requests with canonical error.

⸻

PW-03 Provider request mapping
	•	Transform canonical request → provider-specific format.
	•	Apply provider-specific defaults and limits.
	•	Inject idempotency key if provider supports it.

⸻

PW-04 Provider execution
	•	Call upstream AI provider API.
	•	Handle retries according to provider guidance.
	•	Enforce request timeout and cancellation if supported.

⸻

PW-05 Response normalization
	•	Transform provider response → canonical response schema.
	•	Normalize:
	•	output structure
	•	usage metrics
	•	cost/billing data (actual or estimated).

⸻

PW-06 Error normalization
	•	Catch provider-specific errors.
	•	Map them to canonical error codes.
	•	Persist raw error payload reference.

⸻

PW-07 Result publishing
	•	Publish result event back to Control Plane:
	•	job_id, attempt_id, status
	•	normalized response or error
	•	usage & cost
	•	Ensure at-least-once delivery.

⸻

PW-08 Idempotency & dedupe
	•	Ensure duplicate messages do not trigger duplicate executions.
	•	Support dedupe by (job_id, attempt_id).

⸻

PW-09 Payload handling
	•	Upload large payloads (raw responses, artifacts) to object storage.
	•	Include storage references in result message.

⸻

PW-10 Observability
	•	Emit structured logs with:
	•	job_id, attempt_id, provider, model.
	•	Emit provider-specific metrics:
	•	latency, error rate, rate-limit hits.