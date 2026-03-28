# Prompt Gateway Project Map

## Core Surfaces

- Control plane API:
  - `Prompt Gateway – Control Plane /src/ControlPlane.Api/Program.cs`
  - `Prompt Gateway – Control Plane /src/ControlPlane.Api/Controllers/JobsController.cs`
- Control plane orchestration:
  - `Prompt Gateway – Control Plane /src/ControlPlane.Core/JobOrchestrator.cs`
  - `Prompt Gateway – Control Plane /src/ControlPlane.Core/ResultMessageProcessor.cs`
  - `Prompt Gateway – Control Plane /src/ControlPlane.Core/DispatchOutboxProcessor.cs`
- Provider execution:
  - `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs`
  - `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Services/ProviderMessageProcessor.cs`
  - `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Services/OpenAiClient.cs`
- Lambda entrypoints:
  - `Prompt Gateway Provider - OpenAI/src/Provider.Worker.Lambda/Function.cs`
  - `Prompt Gateway – Control Plane /src/ControlPlane.ResultLambda/Function.cs`
  - `Prompt Gateway – Control Plane /src/ControlPlane.OutboxLambda/Function.cs`
- Infrastructure:
  - `infra/terraform/modules/lambda-processing/main.tf`
  - `infra/terraform/modules/iam/main.tf`
  - `infra/terraform/modules/ecs-service/main.tf`
  - `infra/terraform/environments/dev/main.tf`
- Deployment and ops:
  - `scripts/set-processing-mode.sh`
  - `scripts/promote-lambda-mode.sh`
  - `scripts/smoke-test.sh`

## Hotspots

These files should be treated as high-attention review areas:

- `Prompt Gateway – Control Plane /src/ControlPlane.Core/JobOrchestrator.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs`
- `infra/terraform/modules/iam/main.tf`
- the future Lambda runtime Terraform module(s)

## Recurring Questions

Use these questions to keep reviews consistent:

1. Is the core orchestration still host-agnostic, or is hosting logic leaking inward?
2. Can a client observe an error after the system already persisted work?
3. Are retries clearly separated into transient vs permanent failures?
4. Do dedupe, outbox, and partial batch failure semantics still line up across all runtime paths?
5. Is configuration duplicated across API, worker, and other hosts?
6. Does Terraform still match the intended platform architecture without stale paths accumulating?
7. Are tests covering the control plane, provider path, and failure behavior at the right boundaries?
8. Are rollout checks strong enough for ongoing platform evolution and safer releases?

## Typical Review Commands

```bash
git status --short
rg --files
rg -n "enable_lambda_processing|HostedWorkers|ReportBatchItemFailures|OpenAiRetry|TryStartAsync"
dotnet test "Prompt Gateway – Control Plane /tests/ControlPlane.Core.Tests/ControlPlane.Core.Tests.csproj"
dotnet test "Prompt Gateway Provider - OpenAI/tests/Provider.Worker.Tests/Provider.Worker.Tests.csproj"
```
