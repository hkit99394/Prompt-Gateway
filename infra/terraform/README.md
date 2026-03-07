# Prompt Gateway – Terraform Infrastructure

IaC for the Control Plane and Provider Worker (ECS Fargate, DynamoDB, SQS, S3, etc.).

> **Note:** Module `main.tf` files are skeletons. Implement resources per `docs/DEPLOYMENT_PLAN.md` (T-2.2 through T-2.8) before running `terraform apply`.

## Structure

```
infra/terraform/
├── modules/
│   ├── network/      # VPC, subnets, security groups
│   ├── dynamodb/     # Single table, GSI, TTL
│   ├── sqs/          # Dispatch, result, DLQ
│   ├── s3/           # Prompts and results buckets
│   ├── iam/          # Task roles and policies
│   └── ecs-service/  # ECS cluster, task definitions, services
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

## First-time backend setup

If the S3 bucket and DynamoDB table don't exist yet, run `terraform init` with a local backend first, or create the backend resources manually, then run `terraform init` to migrate state.
