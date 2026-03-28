# async-event-processing

## Mission

Turn queue-processing behavior into Lambda-ready event handling with correct partial failure, dedupe, and batch semantics.

## Use When

- Converting SQS polling loops into triggered handlers
- Defining partial batch failure behavior
- Adjusting dedupe and re-drive semantics
- Updating dispatch/result queue contracts

## Owns

- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Api/ResultQueueProcessor.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Core/Policy/ProviderResultEventContractMapper.cs`
- queue-processing tests in both solutions

## Avoid

- Large domain-rule changes in `JobOrchestrator` without coordination
- Full ownership of infrastructure deployment changes

## Handoff

Deliver runtime handlers and documented failure semantics for `lambda-platform` to wire into AWS.

## Success Checks

- Handler behavior is safe under retries
- Partial batch failure behavior is explicit
- Duplicate and poison-message handling remain deterministic
