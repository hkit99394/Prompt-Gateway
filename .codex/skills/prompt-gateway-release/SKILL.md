---
name: prompt-gateway-release
description: Use when deploying Prompt Gateway with the project release scripts, especially to run first-deploy phase 3 and phase 4, verify the selected ECS or Lambda processing mode, and only commit release-related changes after the live smoke test passes.
---

# Prompt Gateway Release

## When To Use

Use this skill when the user asks to:

- run `scripts/first-deploy-phase3.sh`
- run `scripts/first-deploy-phase4.sh`
- switch or verify ECS versus Lambda processing mode
- promote Lambda mode through the project scripts
- commit release work only after deployment and smoke verification succeed

Do not use this skill for pure code review or architecture analysis.

## Release Workflow

1. Check the repo state first.
Run `git status --short` and confirm what will be part of the release commit.

2. Use the project scripts instead of recreating deployment commands.
The main entry points are:
- `./scripts/first-deploy-phase3.sh`
- `./scripts/first-deploy-phase4.sh`
- `./scripts/set-processing-mode.sh`
- `./scripts/promote-lambda-mode.sh`

3. Prefer the repo defaults unless the user says otherwise.
For this repo, `first-deploy-phase3.sh` defaults to:
- `ENV=dev`
- `AWS_REGION=us-east-1`
- `PROCESSING_MODE=lambda`

4. Treat Phase 3 and Phase 4 as one verification chain.
- Run Phase 3 first.
- If it succeeds, run Phase 4.
- Only describe the deploy as successful if the smoke test reaches job completion and `GET /jobs/{jobId}/result` returns `200`.

5. Do not commit a broken rollout.
- If Phase 3 fails, stop and report the failing step.
- If Phase 4 fails, stop and report the smoke-test diagnostics.
- Only create a git commit after the live deploy and smoke test pass, unless the user explicitly asks otherwise.

6. Keep the rollback story visible.
- Lambda mode is the target runtime for queue-driven processing.
- The ECS provider worker remains rollback infrastructure until the project explicitly retires it.
- The HTTP control plane remains on ECS/ALB.

## Commands

Use the project scripts directly:

```bash
./scripts/first-deploy-phase3.sh
./scripts/first-deploy-phase4.sh
./scripts/set-processing-mode.sh --mode lambda --verify-only --run-smoke-test
./scripts/promote-lambda-mode.sh staging
```

## Notes

- Phase 3 packages Lambda artifacts and applies Terraform changes when Lambda mode is selected.
- Phase 4 uploads the smoke prompt fixture by default and resolves the API key from SSM in `dev` or Secrets Manager in `staging` and `prod`.
- If a smoke test stalls in `Dispatched`, inspect the outbox Lambda, provider Lambda, result-ingestion Lambda, and the dispatch/result queues before assuming the provider call failed.
- When asked to commit after a successful deploy, include the deploy verification result in the final response.
