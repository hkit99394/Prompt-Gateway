# Prompt Gateway Docs Guide

This folder contains a mix of current operational docs, active planning docs, and historical implementation records.

Use this guide to tell which document is the current source of truth.

## Current Source Of Truth

These docs describe the current implemented system or the current active backlog:

| Document | Status | Use for |
|------|------|---------|
| `docs/IMPLEMENTATION_BACKLOG.md` | Active | Current prioritized implementation backlog and next work |
| `infra/terraform/README.md` | Current | Current infrastructure shape, Terraform usage, Lambda packaging, and promotion flow |
| `Prompt Gateway – Control Plane /src/ControlPlane.Api/README.md` | Current | Current API operations runbook and request contract |
| `Prompt Gateway Provider - OpenAI/README.md` | Current | Current provider worker behavior and local run/config guidance |

## Historical Plan Records

These docs are mainly kept as planning history and design record. They may still be useful for context, but they should not be treated as the primary description of the current implementation unless they are explicitly updated.

| Document | Status | Notes |
|------|------|---------|
| `docs/DEPLOYMENT_PLAN.md` | Historical plan record | Original deployment and CI/CD plan; parts of it are now superseded by implemented repo state |
| `docs/IMPLEMENTATION_BACKLOG_ACHIEVED.md` | Historical record | Archived completed phase backlog as of March 28, 2026 |
| `docs/ECS_TO_LAMBDA_PLAN.md` | Planning record | Migration strategy and sequencing guidance |
| `docs/P1_CONTRACT_FIX_PLAN.md` | Historical change record | Records the contract-fix work and intended rollout |
| `docs/P2_LIFECYCLE_TIMEOUT_PLAN.md` | Historical change record | Records lifecycle/timeout hardening work |
| `docs/P3_RETRY_POLICY_PLAN.md` | Historical change record | Records retry-hardening work |

## Suggested Doc Rules

- Put current operational truth close to the code or infra it describes.
- Keep `docs/` focused on cross-cutting plans, migration records, and backlog tracking.
- Add a short status banner at the top of every planning doc:
  - `Current`
  - `Active plan`
  - `Historical record`
- When a plan is implemented, do not keep editing it as if it were the live runbook.
  Update the current runbook/README instead, and mark the plan as historical.

## Recommended Ongoing Structure

- `docs/README.md`
  - Index and status guide for all docs.
- Code-adjacent READMEs
  - Current operational truth.
- `docs/IMPLEMENTATION_BACKLOG.md`
  - Current future work.
- `docs/IMPLEMENTATION_BACKLOG_ACHIEVED.md`
  - Archived completed phase backlog.
- `docs/*_PLAN.md`
  - Planning or historical records only.
