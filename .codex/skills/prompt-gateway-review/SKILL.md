---
name: prompt-gateway-review
description: Use when reviewing the Prompt Gateway repository for architecture, code health, operational risks, test coverage, rollout readiness, long-term maintainability, or recurring project check-ins.
---

# Prompt Gateway Review

## Overview

Use this skill for repeatable long-term reviews of the Prompt Gateway project. It is tuned for the control plane, the OpenAI provider worker, the Terraform stack, and the architectural health of the system over time.

## When To Use

Use this skill when the user asks for:

- a project analysis, repo walkthrough, or architecture summary
- a recurring review of progress, quality, risks, or readiness
- a runtime, operational, or deployment health review
- a long-term maintainability, scalability, or platform-direction review
- a code review focused on regressions, missing tests, rollout safety, or contract drift

Do not use this skill for narrow implementation tasks unless the user explicitly wants a review mindset.

## Review Workflow

1. Start with repo context.
Read `AGENTS.md`, `.agents/README.md`, the main READMEs, and the migration docs before drawing conclusions. Use `rg --files` to map the repo quickly.

2. Map the running architecture.
Confirm the current control-plane API path, orchestration core, provider execution path, infrastructure wiring, and deployment/runtime boundaries. For this repo, the recurring anchors are:
- `Prompt Gateway – Control Plane /src/ControlPlane.Api/Program.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.Core/JobOrchestrator.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Services/ProviderMessageProcessor.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker/Worker.cs`
- `Prompt Gateway Provider - OpenAI/src/Provider.Worker.Lambda/Function.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.ResultLambda/Function.cs`
- `Prompt Gateway – Control Plane /src/ControlPlane.OutboxLambda/Function.cs`
- `infra/terraform/modules/lambda-processing/main.tf`
- `infra/terraform/modules/iam/main.tf`
- `scripts/set-processing-mode.sh`

3. Check health signals before making claims.
Prefer concrete signals over inference:
- `git status --short`
- targeted `dotnet test` runs for the control plane and provider worker
- configuration/readiness wiring in API and Lambda entrypoints
- deployment scripts and Terraform environment/module wiring

4. Review the repo through the project's real risk areas.
Prioritize:
- intake idempotency and partial-failure behavior
- outbox correctness and retry semantics
- provider dedupe and result publication behavior
- Lambda batch failure handling
- configuration duplication between hosts
- IAM least privilege and secret loading
- rollout verification and smoke-test coverage
- long-term modularity, ownership boundaries, and platform drift

5. Respect project-local collaboration rules.
If delegation is explicitly requested, follow `AGENTS.md` and `.agents/README.md`:
- prefer architecture-based ownership
- keep one owner per hotspot file
- add `release-verification` for rollout-sensitive changes
- prefer the roster that best matches the architectural area being reviewed

## Expected Output

For a general project review, organize the answer into:

- overall assessment
- strengths
- main risks or findings
- recommended next moves

When the user asks for a review, default to a code-review mindset:

- findings first, ordered by severity
- include file references and concrete behavior risks
- keep summary short
- call out missing tests or rollout gaps

Always distinguish between:

- confirmed facts from the repo
- inferences about operational behavior
- unverified areas that would need AWS or deployment access

## Commands

Prefer fast local inspection:

```bash
rg --files
rg -n "HostedWorkers|enable_lambda_processing|ReportBatchItemFailures|TryStartAsync|Retry"
dotnet test "Prompt Gateway – Control Plane /tests/ControlPlane.Core.Tests/ControlPlane.Core.Tests.csproj"
dotnet test "Prompt Gateway Provider - OpenAI/tests/Provider.Worker.Tests/Provider.Worker.Tests.csproj"
```

## Project Notes

- The repo is centered on a control plane plus a provider worker, with Terraform-managed AWS infrastructure.
- Prefer long-term architecture and operational clarity over one-off migration framing.
- The single-owner hotspot files listed in `AGENTS.md` deserve extra care during change reviews.
- Use the reference file for a compact project map and recurring review prompts.

## References

- Read [references/project-map.md](references/project-map.md) for the repo layout, hotspots, and recurring review questions.
