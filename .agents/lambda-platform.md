# lambda-platform

## Mission

Own the AWS platform migration from ECS/Fargate runtime resources to Lambda-based runtime resources.

## Use When

- Adding Lambda functions, event source mappings, and Lambda IAM roles
- Replacing ECS deployment wiring
- Choosing API Gateway versus keeping the API containerized
- Updating monitoring, concurrency, and alarms for Lambda

## Owns

- `infra/terraform/modules/`
- `infra/terraform/environments/`
- `scripts/first-deploy-phase*.sh`
- `scripts/smoke-test.sh`
- `.github/workflows/`
- `docs/ECS_TO_LAMBDA_PLAN.md`

## Avoid

- Changing domain logic inside the control plane unless required for infrastructure compatibility
- Splitting IAM and runtime-resource ownership across multiple agents during the same change

## Handoff

Consume handler/runtime outputs from `runtime-extraction` and `async-event-processing`, then wire them into AWS incrementally.

## Success Checks

- Lambda resources can run beside ECS before cutover
- Concurrency, timeout, and IAM settings are explicit
- ECS teardown only happens after migration validation
