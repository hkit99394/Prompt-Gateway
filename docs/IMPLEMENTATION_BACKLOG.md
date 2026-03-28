> Status: Active plan
>
> Previous completed backlog: `docs/IMPLEMENTATION_BACKLOG_ACHIEVED.md`

# Prompt Gateway – Implementation Backlog

This document tracks the next work after the original migration and runtime-hardening backlog was archived on March 28, 2026.

## Status Summary

| Phase | Goal | Outcome |
|------|------|---------|
| 8 | Add API Gateway edge auth and inline prompt intake | Implemented locally on March 28, 2026; deployment verification and operational follow-through remain |
| 9 | Harden rollout, testing, and auth posture | Active |

## Recently Completed

- Added a Lambda-backed API Gateway request authorizer for the Lambda HTTP edge while keeping app-level API-key auth in place as defense in depth.
- Added inline prompt support through `promptText` alongside the existing prompt-reference contract (`inputRef`, `promptKey`, `promptS3Key`).
- Kept `/health`, `/ready`, and Swagger routes public at the API Gateway edge to preserve current operational behavior.
- Updated Lambda packaging and Terraform wiring to include the new authorizer artifact and role.

---

## Phase 9 – Rollout And Contract Hardening

### PG-901 Verify API Gateway authorizer rollout

- Status: Active
- Primary owner: `lambda-platform`
- Sidecar: `release-verification`
- Priority: High
- Dependencies: Phase 8 implementation

**Problem**

The authorizer is now in the repo, but the environments still need a promotion path and concrete verification evidence before it can be treated as fully operational.

**Scope**

- Deploy the authorizer through `dev`, `staging`, and `prod` alongside the Lambda HTTP edge.
- Verify protected routes fail fast at API Gateway when `X-API-Key` is missing or invalid.
- Verify `/health`, `/ready`, and Swagger remain reachable without auth.

**Acceptance criteria**

- `GET /jobs` and `POST /jobs` return authorization failures before the app executes when the key is missing or invalid.
- `GET /health`, `GET /ready`, `GET /swagger`, and `GET /swagger/v1/swagger.json` still return successfully without auth.
- Deployment notes capture rollback steps if the authorizer blocks expected traffic.

### PG-902 Extend smoke and integration coverage for the new contract

- Status: Active
- Primary owner: `control-plane-core`
- Sidecar: `release-verification`
- Priority: High
- Dependencies: PG-901

**Problem**

Current automated verification still centers on prompt references and does not exercise the new edge-authorizer behavior.

**Scope**

- Add smoke coverage for:
  - unauthorized edge rejection
  - authorized `POST /jobs`
  - `promptText`-based job creation
  - prompt-reference job creation
- Add at least one end-to-end verification path for Lambda HTTP mode with the authorizer enabled.

**Acceptance criteria**

- Automated verification proves both prompt submission modes work.
- Automated verification proves unauthorized requests fail predictably at the edge.
- Smoke diagnostics remain easy to read during deploys and rollbacks.

### PG-903 Add inline prompt guardrails

- Status: Active
- Primary owner: `control-plane-core`
- Priority: Medium
- Dependencies: Phase 8 implementation

**Problem**

Inline prompt support increases the chance of oversized request bodies, accidental sensitive prompt logging, and unclear client limits.

**Scope**

- Define maximum inline prompt size and failure behavior.
- Audit logs and diagnostics so raw inline prompt text is not emitted accidentally.
- Document when clients should use `promptText` versus prompt references.

**Acceptance criteria**

- The API enforces a clear inline prompt size limit.
- Failure responses are explicit when the inline prompt exceeds the supported size.
- Logs and troubleshooting guidance avoid echoing raw user prompt content by default.

### PG-904 Decide the long-term auth boundary

- Status: Active
- Primary owner: `lambda-platform`
- Priority: Medium
- Dependencies: PG-901

**Problem**

The system now has both API Gateway authorizer checks and app-layer API-key checks. That is a good transitional posture, but the long-term target should be deliberate.

**Scope**

- Decide whether to keep defense in depth permanently or simplify later.
- Evaluate client ergonomics, rotation flow, operational complexity, and blast radius.
- Document the chosen steady-state model.

**Acceptance criteria**

- The repo documents whether edge auth plus app auth is permanent or transitional.
- Key rotation guidance reflects the chosen boundary.
- Future auth work is framed around one explicit target model instead of an implicit default.
