# Prompt Gateway – Terraform Infrastructure

IaC for the Control Plane and Provider Worker (ECS Fargate, Lambda, DynamoDB, SQS, S3, etc.).

> **Note:** Ensure modules are implemented per `docs/DEPLOYMENT_PLAN.md` (T-2.2 through T-2.8) before running `terraform apply`.

## Structure

```
infra/terraform/
├── modules/
│   ├── network/      # VPC, subnets, security groups
│   ├── dynamodb/     # Single table, GSI, TTL
│   ├── sqs/          # Dispatch, result, DLQ
│   ├── s3/           # Prompts and results buckets
│   ├── iam/          # Task roles and policies
│   ├── ecs-service/  # ECS cluster, task definitions, services
│   └── lambda-processing/ # Lambda functions, event mappings, schedule
└── environments/
    ├── dev/
    ├── staging/
    └── prod/
```

## Prerequisites

1. **Terraform** >= 1.0
2. **AWS credentials** configured (profile or env vars)
3. **Backend resources** (for remote state):
   - S3 bucket per environment (e.g. `prompt-gateway-terraform-state-dev`)
   - DynamoDB table per environment for locking (e.g. `prompt-gateway-terraform-locks-dev`, `-staging`, `-prod`)

## Usage

```bash
cd environments/dev
cp dev.tfvars.example dev.tfvars   # first time only - edit if needed
terraform init
terraform plan -var-file=dev.tfvars
terraform apply -var-file=dev.tfvars
```

**Note:** `*.tfvars` files are gitignored. Copy from `*.tfvars.example` and never commit real `.tfvars`—they may contain secrets.

## Lambda Packaging

When `enable_lambda_processing` is enabled in an environment, Terraform expects these zip artifacts to exist:

```bash
./scripts/package-lambda-artifacts.sh
```

This produces:

- `artifacts/provider-worker-lambda.zip`
- `artifacts/control-plane-result-lambda.zip`
- `artifacts/control-plane-outbox-lambda.zip`

The environment variables `provider_lambda_package_path`, `result_lambda_package_path`, and `outbox_lambda_package_path` can override those defaults when needed.

## First-time backend setup

Run the bootstrap script before first `terraform init`:

```bash
./scripts/bootstrap-terraform-backend.sh dev
```

Or create the backend resources manually:
- S3 bucket: `prompt-gateway-terraform-state-{env}`
- DynamoDB table: `prompt-gateway-terraform-locks-{env}`

## First-deploy Phase 1 (T-5.1)

To deploy dev infrastructure and verify:

```bash
./scripts/bootstrap-terraform-backend.sh dev   # if not done
./scripts/first-deploy-phase1.sh
```

Options: `--plan-only` (plan without apply), `--skip-verify` (skip post-apply verification).
