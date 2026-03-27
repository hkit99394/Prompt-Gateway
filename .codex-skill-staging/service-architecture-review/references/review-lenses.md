# Review Lenses

Use these prompts to sharpen the review without bloating the final output.

## Runtime Boundaries

- Where does core logic stop and host-specific wiring begin?
- Are there multiple hosts rebuilding the same dependency graph or options?
- Can business logic be reused across ECS, Lambda, CLI, and tests without drift?

## Async Correctness

- Is there a clear idempotency strategy for intake, dispatch, and result ingestion?
- What happens if persistence succeeds but downstream dispatch fails?
- Are retries safe for both transient and permanent failures?
- Does the system preserve at-least-once semantics without multiplying side effects?

## Data and Queue Semantics

- Are queue visibility timeouts aligned with processing time?
- Does dedupe handle duplicate-completed vs duplicate-in-progress cases separately?
- Are outbox or lease patterns efficient enough for expected throughput?
- Do storage access patterns create hot partitions or expensive filtered scans?

## Operational Safety

- Does readiness reflect the dependencies that matter for the active runtime mode?
- Are primary failure signals visible before work reaches the DLQ?
- Can operators verify which runtime mode is active and whether the inactive path is truly off?
- Are deployments immutable enough to support rollback and incident analysis?

## Migration Safety

- Is the target architecture already reflected in the code, or only in docs?
- Which components can move first with the least blast radius?
- What temporary duplication exists during migration, and how will it be retired?
- What validation is needed at each phase before decommissioning the old path?
