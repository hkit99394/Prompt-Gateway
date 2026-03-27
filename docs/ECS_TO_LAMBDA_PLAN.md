# ECS To Lambda Plan

> **Document status: Planning record**
>
> This file describes migration strategy and decision sequencing. It is useful for architecture context, but it is not the primary source of truth for the current implementation state.
>
> Use these docs first for the current implemented system:
>
> - `docs/README.md`
> - `docs/IMPLEMENTATION_BACKLOG.md`
> - `infra/terraform/README.md`
> - `Prompt Gateway – Control Plane /src/ControlPlane.Api/README.md`
> - `Prompt Gateway Provider - OpenAI/README.md`

## Short answer

Yes, this project can move from ECS/Fargate to Lambda, but not as a direct lift-and-shift.

The cleanest path is:

1. Move the queue consumers to Lambda first.
2. Split background processing out of the ASP.NET Core API host.
3. Decide whether the HTTP control plane should also move to Lambda or stay as a small container service.

## What the repo shows today

### Control plane API

- The ASP.NET Core API runs HTTP endpoints in [`Prompt Gateway – Control Plane /src/ControlPlane.Api/Program.cs`](/Users/jacktam/Documents/Project/Prompt Gateway/Prompt Gateway – Control Plane /src/ControlPlane.Api/Program.cs).
- That same host also runs two `BackgroundService`s:
  - [`Prompt Gateway – Control Plane /src/ControlPlane.Api/OutboxWorker.cs`](/Users/jacktam/Documents/Project/Prompt Gateway/Prompt Gateway – Control Plane /src/ControlPlane.Api/OutboxWorker.cs)
  - [`Prompt Gateway – Control Plane /src/ControlPlane.Api/ResultQueueWorker.cs`](/Users/jacktam/Documents/Project/Prompt Gateway/Prompt Gateway – Control Plane /src/ControlPlane.Api/ResultQueueWorker.cs)
- Those loops repeatedly call single-step processors:
  - [`Prompt Gateway – Control Plane /src/ControlPlane.Core/DispatchOutboxProcessor.cs`](/Users/jacktam/Documents/Project/Prompt Gateway/Prompt Gateway – Control Plane /src/ControlPlane.Core/DispatchOutboxProcessor.cs)
  - [`Prompt Gateway – Control Plane /src/ControlPlane.Api/ResultQueueProcessor.cs`](/Users/jacktam/Documents/Project/Prompt Gateway/Prompt Gateway – Control Plane /src/ControlPlane.Api/ResultQueueProcessor.cs)

### Provider worker

- The provider side is a long-running worker host in [`Prompt Gateway Provider - OpenAI/src/Provider.Worker.Host/Program.cs`](/Users/jacktam/Documents/Project/Prompt Gateway/Prompt Gateway Provider - OpenAI/src/Provider.Worker.Host/Program.cs).
- Its worker loop polls SQS, calls OpenAI, writes payloads to S3, updates dedupe state in DynamoDB, and publishes results in [`Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs`](/Users/jacktam/Documents/Project/Prompt Gateway/Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs).

### Infrastructure

- Terraform is explicitly built around ECS, ALB, ECR, and task roles in:
  - [`infra/terraform/modules/ecs-service/main.tf`](/Users/jacktam/Documents/Project/Prompt Gateway/infra/terraform/modules/ecs-service/main.tf)
  - [`infra/terraform/modules/iam/main.tf`](/Users/jacktam/Documents/Project/Prompt Gateway/infra/terraform/modules/iam/main.tf)
- Core data and messaging dependencies are already serverless-friendly:
  - SQS
  - DynamoDB
  - S3

## Feasibility by component

### Best Lambda candidates

#### 1. Provider worker

This is the strongest Lambda candidate.

Why it fits:

- Input already arrives through SQS.
- Work is naturally message-based.
- Existing dedupe logic is useful for Lambda retries.
- Result publication is already asynchronous.

Required changes:

- Refactor the worker so message handling is callable per SQS batch or per message.
- Replace the `BackgroundService` polling loop with an SQS-triggered Lambda handler.
- Return partial batch failures so only failed records retry.
- Set Lambda timeout, SQS visibility timeout, and batch size to match worst-case OpenAI latency.
- Add reserved concurrency so Lambda scale does not exceed OpenAI rate limits or budget.

#### 2. Result queue ingestion

This is also a good Lambda candidate.

Why it fits:

- The current processor already handles one message at a time and delegates business logic into `JobOrchestrator`.
- SQS-triggered Lambda is a simpler fit than a permanent worker loop.

Required changes:

- Wrap `ResultQueueProcessor` logic in an SQS handler.
- Support partial batch failure behavior.
- Keep idempotency and duplicate protection in DynamoDB.

### Good candidate, but needs a design choice

#### 3. Dispatch outbox processing

This can move to Lambda, but there are two patterns:

Option A: scheduled poller Lambda

- Lowest code churn.
- A scheduled Lambda periodically calls `DispatchOutboxProcessor.ProcessOnceAsync` or a batch equivalent.
- Easy transition, but it keeps polling behavior.

Option B: event-driven outbox Lambda

- Better long-term architecture.
- Trigger on new outbox records, likely via DynamoDB Streams.
- Publish to the dispatch queue and mark the outbox item as dispatched.
- Less idle cost and less operational lag than polling.

Recommendation:

- Start with scheduled polling if speed matters.
- Move to DynamoDB Streams once the Lambda split is stable.

### Possible, but not the first move

#### 4. Control plane HTTP API

This can run on Lambda behind API Gateway, but it is not just an infrastructure swap because the current API host is also responsible for background processing.

It becomes straightforward only after the hosted workers are extracted.

What changes if the API moves too:

- The HTTP portion of [`Prompt Gateway – Control Plane /src/ControlPlane.Api/Program.cs`](/Users/jacktam/Documents/Project/Prompt Gateway/Prompt Gateway – Control Plane /src/ControlPlane.Api/Program.cs) stays, but hosted background services are removed.
- Health checks need to be reconsidered because ECS liveness/readiness concepts do not map directly to Lambda.
- Swagger exposure may need a deliberate choice for production.
- Authentication remains compatible, but API Gateway may take over some edge concerns.

Recommendation:

- Do not migrate the API to Lambda first.
- First split the background concerns out of the web host.
- Then decide whether API Gateway + Lambda is worth the cold-start and integration tradeoffs.

## Recommended target architecture

### Recommended near-term target

- API:
  - Either keep on ECS temporarily, or move later to Lambda behind API Gateway.
- Dispatch outbox:
  - Lambda.
- Provider execution:
  - Lambda triggered by dispatch SQS.
- Result ingestion:
  - Lambda triggered by result SQS.
- Data plane:
  - Keep DynamoDB, SQS, and S3.

This gives most of the Lambda operational benefit without forcing the whole system to move at once.

## Migration phases

### Phase 0: Decision checkpoint

Decide between:

- Hybrid target:
  - API stays containerized for now.
  - Workers move to Lambda.
- Full Lambda target:
  - API also moves to Lambda.

Recommendation:

- Choose hybrid first.

### Phase 1: Refactor runtime boundaries

Goal:
Make business logic host-agnostic.

Work:

- Extract provider message handling from `Worker` into a dedicated application service.
- Extract result queue message handling from `ResultQueueProcessor` into a handler callable by Lambda.
- Extract outbox dispatch into a handler that can process one or many work items.
- Keep shared orchestration logic in existing core libraries.

Exit criteria:

- The project can run handlers without a `BackgroundService` loop.
- Existing unit tests still pass.

### Phase 2: Introduce Lambda entry points

Goal:
Add Lambda hosts without deleting ECS yet.

Work:

- Add one Lambda function for provider processing from the dispatch queue.
- Add one Lambda function for result ingestion from the result queue.
- Add one Lambda function for outbox dispatch, either scheduled or stream-driven.
- Create Lambda-specific configuration and IAM roles.

Exit criteria:

- All worker paths can run in parallel with or instead of ECS in a dev environment.

### Phase 3: Update infrastructure

Goal:
Replace ECS worker infrastructure with Lambda infrastructure.

Work:

- Add Terraform modules for Lambda functions, event source mappings, log groups, and alarms.
- Remove ECS worker service definitions once Lambda is proven.
- Rework IAM from task execution/task roles to Lambda execution roles.
- Revisit networking:
  - If Lambda does not need VPC-only resources, keep functions outside the VPC to avoid NAT cost and simplify egress to OpenAI.
  - If VPC placement is required, keep NAT and size concurrency carefully.

Exit criteria:

- No queue-processing path depends on ECS.
- Mode verification and smoke-test gates exist for the active runtime.
- Monitoring covers Lambda errors, throttles, duration pressure, and primary queue backlog.

### Phase 4: Decide on the API

Goal:
Choose whether the control plane HTTP API also leaves ECS.

Option A: keep API on ECS

- Lowest risk.
- Good if you want stable HTTP latency and already accept container operations for one service.

Option B: move API to Lambda + API Gateway

- Removes ECS and ALB entirely if no container services remain.
- Requires Lambda-compatible ASP.NET Core hosting and API Gateway integration.
- Worth it if traffic is bursty and operational simplicity matters more than cold-start sensitivity.

Exit criteria:

- A deliberate platform choice is made for the HTTP edge.

### Phase 5: Decommission ECS

Only after all traffic and background work have moved.

Work:

- Remove ECS cluster, services, task definitions, ALB, and ECR repos if no longer needed.
- Update deployment scripts and runbooks.
- Update monitoring dashboards and alarms to Lambda metrics and SQS backlog metrics.

## Rollout gates

Before promoting Lambda mode between environments:

1. Run `./scripts/set-processing-mode.sh --mode lambda --verify-only --run-smoke-test`.
2. Confirm no CloudWatch alarms are active for:
   - Lambda `Errors`
   - Lambda `Throttles`
   - Lambda duration-pressure alarms
   - dispatch/result queue backlog or oldest-message-age alarms
   - DLQ depth
3. Record the exact image tags and Lambda package artifacts used for the deploy.
4. Keep the rollback command prepared:
   - `./scripts/set-processing-mode.sh --mode ecs`

Promotion expectations:

- Dev proves the runtime wiring and smoke path.
- Staging proves the same build set outside the bootstrap environment.
- Prod only moves after staging evidence exists for the exact same artifacts.

Rollback expectations:

- If verification fails, stop the promotion immediately.
- Switch back to ECS mode, rerun `--verify-only --run-smoke-test`, and attach the failing smoke-test and alarm evidence to the deployment record.

## Main risks

### 1. OpenAI latency vs Lambda timeout

- Lambda has a maximum execution duration.
- If a single provider call plus prompt loading plus payload storage can approach that limit, Lambda may be a poor fit for that path.
- This should be tested with real prompts before committing.

### 2. Concurrency explosion

- ECS gives explicit worker counts.
- SQS-triggered Lambda can scale much faster.
- Without reserved concurrency and queue tuning, cost and provider throttling can spike.

### 3. Outbox correctness

- The outbox pattern protects dispatch reliability.
- Replacing the current polling worker with Lambda must preserve at-least-once semantics and idempotent dispatch handling.

### 4. API cold starts

- If the control plane API moves to Lambda, first-request latency may increase.
- This matters more if clients expect consistently low latency for `POST /jobs` and read APIs.

## Suggested implementation order

1. Provider worker to Lambda.
2. Result queue processor to Lambda.
3. Outbox dispatcher to Lambda.
4. Remove hosted workers from the ASP.NET Core API.
5. Reassess whether the API should stay on ECS or move to Lambda.

## Recommendation

The project should not move from ECS to Lambda in one step.

The safest and highest-value plan is to move the worker paths first and keep the control plane API separate until that is stable. If those worker migrations go well, the remaining API move becomes a smaller product and latency decision instead of a deep architectural rewrite.
