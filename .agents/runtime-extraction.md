# runtime-extraction

## Mission

Refactor long-running worker loops into host-agnostic handlers that can be called from Lambda, tests, or temporary container hosts.

## Use When

- A `BackgroundService` loop needs to be split into per-message or per-batch handling.
- Queue-processing logic needs to run without a forever loop.
- The migration phase is about extraction before infrastructure cutover.

## Owns

- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Api/ResultQueueProcessor.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Core/DispatchOutboxProcessor.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Api/ResultQueueWorker.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Api/OutboxWorker.cs`

## Avoid

- Large Terraform changes
- IAM policy design
- Control-plane lifecycle rule changes unless coordinated with `control-plane-core`

## Handoff

Deliver handler-shaped entry points with clear inputs, outputs, and error semantics. Leave host bootstrapping to `lambda-platform`.

## Success Checks

- Core logic can run without `BackgroundService`
- Existing unit tests still pass or are updated to the new handler boundary
- Polling concerns are isolated from business logic
