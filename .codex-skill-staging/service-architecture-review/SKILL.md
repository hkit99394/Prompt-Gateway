---
name: service-architecture-review
description: Review a service-oriented codebase and produce a structured architecture review, prioritized risk register, and concrete refactor plan. Use when evaluating backend systems, queue-driven workflows, migration plans, cloud runtime splits, deployment safety, or operational readiness across application code and infrastructure.
---

# Service Architecture Review

## Overview

Use this skill to assess how a service-based system is structured, where the real operational and design risks sit, and what sequence of changes would improve it safely.

This skill is especially useful for backend platforms that span application code, async processing, infrastructure, deployment scripts, and migration work such as ECS-to-Lambda, monolith-to-services, or polling-to-event-driven transitions.

## When To Use It

Use this skill when the user wants one or more of the following:

- an architecture review of a repository or subsystem
- a risk register with severity, likelihood, and mitigation guidance
- a concrete refactor or migration plan
- a review of runtime boundaries between HTTP, workers, queues, functions, and data stores
- a readiness review for rollout, rollback, observability, or operational safety

Do not use this skill for narrow framework-only questions when an existing framework skill is a better fit. If the task is mostly about ASP.NET Core API composition or middleware behavior, combine this skill with `$aspnet-core` or defer to it for framework-specific guidance.

## Output Contract

Default to producing these sections unless the user asks for a smaller scope:

1. `Architecture Review`
2. `Risk Register`
3. `Concrete Refactor Plan`

Keep the output decision-oriented. The goal is to help the user decide what matters, what is risky, and what to change next.

## Workflow

### 1. Map the system

Start by identifying:

- top-level apps, services, workers, functions, and infrastructure modules
- entrypoints and host composition
- persistent stores, queues, object storage, and external provider integrations
- deployment/runtime modes such as ECS, Lambda, containerized jobs, cron, or local workers

Prefer concrete repo evidence over inference.

### 2. Trace the hot paths

Read the main execution paths end to end:

- request intake
- orchestration/state transitions
- dispatch/outbox behavior
- async worker processing
- retries, dedupe, idempotency, and finalization
- deployment or mode-switch scripts if they materially affect behavior

Focus on where correctness depends on multiple components cooperating.

### 3. Inspect runtime boundaries

Look for coupling or duplication across:

- web hosts vs background workers
- ECS/container hosts vs Lambda/functions
- core logic vs cloud/provider adapters
- infrastructure modules vs application assumptions

Pay attention to repeated configuration parsing, duplicated dependency wiring, and host-specific branches that can drift over time.

### 4. Check operability

Review how the system behaves in production:

- health/readiness semantics
- alarms, DLQ handling, and backlog visibility
- concurrency controls
- retry behavior and timeout alignment
- deployment immutability and rollback safety
- observability coverage for the active runtime model

If the repo supports multiple runtime modes, assess whether monitoring and verification cover both.

### 5. Check maintainability

Assess:

- clarity of ownership boundaries
- hotspot files with too many responsibilities
- whether tests cover business rules and runtime glue
- whether core logic is reusable across hosts
- whether rollout scripts and docs match the code

### 6. Produce the review

Structure the final review as:

- a concise architecture assessment by subsystem
- a prioritized risk register
- a staged refactor plan with dependencies and success criteria

Use file references for important claims. Prefer specific findings over generic advice.

## Review Lenses

Apply these lenses as relevant:

- correctness
- idempotency
- failure handling
- operability
- observability
- deployability
- rollback safety
- scaling characteristics
- ownership and change isolation

Not every review needs every lens, but async and migration-heavy systems usually need most of them.

## Risk Register Guidance

Each meaningful risk should include:

- a short label
- severity
- likelihood
- why it matters
- the concrete evidence in code or infra
- the mitigation direction

Prefer risks that can change engineering priorities, rollout plans, or incident likelihood. Do not fill the register with low-signal style observations.

## Refactor Plan Guidance

A good refactor plan should:

- start with the highest-leverage structural issues
- separate prerequisite work from follow-on improvements
- preserve working behavior during migration
- include success criteria or verification expectations
- avoid requiring a big-bang rewrite unless the repo genuinely forces one

When there is a migration already in progress, align the plan to the repo’s actual direction instead of proposing a competing architecture.

## References

- For review lenses and heuristics, see [references/review-lenses.md](references/review-lenses.md).
- For the recommended report structure, see [references/output-template.md](references/output-template.md).
