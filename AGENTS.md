# Project Agents

This repository keeps project-local subagent specs in `.agents/`.

Use these rules when delegating work:

- Prefer architecture-based ownership over SDLC-only ownership.
- Assign one primary owner for each hotspot file.
- Add `release-verification` as a sidecar when the change needs rollout safety, smoke tests, or migration checks.
- During the ECS-to-Lambda migration, prefer the Lambda roster in `.agents/README.md`.

Hotspots that should have a single owner at a time:

- `Prompt Gateway – Control Plane /src/ControlPlane.Core/JobOrchestrator.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs`
- `infra/terraform/modules/iam/main.tf`
- the future Lambda runtime Terraform module(s)

Agent index:

- `.agents/README.md`
- `.agents/registry.json`
