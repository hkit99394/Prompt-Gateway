# Prompt Gateway – Deployment & CI/CD Plan

This document describes the target architecture, infrastructure-as-code layout, CI/CD workflows, and first-deploy procedures for the Prompt Gateway Control Plane and Provider Worker. Each section is broken into detailed steps and tasks.

> **Implementation status:** T-2.1 creates the Terraform directory structure and skeleton files only. Module `main.tf` files contain placeholder comments with no resource definitions; outputs return `null`. **`terraform apply` will fail until T-2.2 through T-2.8 are implemented.** Complete the module implementation tasks before deploying.

---

## 1. Target Architecture

| Component | Technology |
|-----------|------------|
| **Compute** | ECS Fargate |
| **Database** | DynamoDB (single-table design) |
| **Queues** | SQS (dispatch, result, DLQ) |
| **Storage** | S3 (prompts, results) |
| **Indexing** | DynamoDB GSI (`gsi1pk` / `gsi1sk`) for job listing |
| **TTL** | DynamoDB TTL enabled for job/event/outbox cleanup |

**Services:**
- **Control Plane API** – REST API, job orchestration, outbox processor
- **Provider Worker (OpenAI)** – Consumes dispatch queue, calls OpenAI, publishes results

---

## 2. Infrastructure-as-Code (IaC) – Detailed Tasks

### 2.1 Repository structure

| Task | Description |
|------|--------------|
| T-2.1.1 | Create `infra/terraform/` directory |
| T-2.1.2 | Create `infra/terraform/modules/` with subdirs: `network`, `dynamodb`, `sqs`, `s3`, `ecs-service`, `iam` |
| T-2.1.3 | Create `infra/terraform/environments/dev`, `staging`, `prod` |
| T-2.1.4 | Add `main.tf`, `variables.tf`, `outputs.tf` per module |
| T-2.1.5 | Add `backend.tf` in each environment (S3 + DynamoDB for state) |

### 2.2 Network module

| Task | Description |
|------|--------------|
| T-2.2.1 | Define VPC with CIDR (e.g. `10.0.0.0/16`) |
| T-2.2.2 | Create public subnets (2+ AZs) for ALB |
| T-2.2.3 | Create private subnets (2+ AZs) for ECS tasks |
| T-2.2.4 | Create Internet Gateway, attach to VPC |
| T-2.2.5 | Create NAT Gateway(s) in public subnets |
| T-2.2.6 | Configure route tables: public → IGW, private → NAT |
| T-2.2.7 | Create security group for ALB (443 inbound, outbound all) |
| T-2.2.8 | Create security group for ECS API (inbound from ALB only) |
| T-2.2.9 | Create security group for ECS worker (outbound to SQS, S3, OpenAI) |
| T-2.2.10 | Output: `vpc_id`, `private_subnet_ids`, `alb_security_group_id`, `ecs_api_security_group_id`, `ecs_worker_security_group_id` |

### 2.3 DynamoDB module

| Task | Description |
|------|--------------|
| T-2.3.1 | Define table with `pk` (string) and `sk` (string) as HASH/RANGE keys |
| T-2.3.2 | Add GSI: `gsi1pk` (HASH), `gsi1sk` (RANGE) – name: `JobListIndex` (or configurable) |
| T-2.3.3 | Enable TTL on attribute `ttl` |
| T-2.3.4 | Set billing mode (on-demand or provisioned) |
| T-2.3.5 | Add server-side encryption (AWS managed key) |
| T-2.3.6 | Output: `table_name`, `table_arn`, `gsi_name` |

### 2.4 SQS module

| Task | Description |
|------|--------------|
| T-2.4.1 | Create DLQ (dead-letter queue) |
| T-2.4.2 | Create dispatch queue with redrive policy → DLQ (maxReceiveCount: 3) |
| T-2.4.3 | Create result queue with redrive policy → DLQ |
| T-2.4.4 | Set visibility timeout (e.g. 300s for worker processing) |
| T-2.4.5 | Set message retention (e.g. 14 days) |
| T-2.4.6 | Output: `dispatch_queue_url`, `result_queue_url`, `dlq_url`, `dispatch_queue_arn`, `result_queue_arn` |

### 2.5 IAM module

| Task | Description |
|------|--------------|
| T-2.5.1 | Create IAM role for ECS task execution (pull images, write logs) |
| T-2.5.2 | Attach `AmazonECSTaskExecutionRolePolicy` |
| T-2.5.3 | Create IAM role for Control Plane API task |
| T-2.5.4 | Policy: DynamoDB (GetItem, PutItem, UpdateItem, DeleteItem, Query, BatchWriteItem, TransactWriteItems) on table |
| T-2.5.5 | Policy: SQS (SendMessage, ReceiveMessage, DeleteMessage, GetQueueAttributes) on dispatch + result queues |
| T-2.5.6 | Policy: S3 (GetObject, PutObject) on prompts + results buckets |
| T-2.5.7 | Policy: Secrets Manager (GetSecretValue) for API keys |
| T-2.5.8 | Policy: SSM (GetParameter) for config |
| T-2.5.9 | Create IAM role for Provider Worker task (SQS, S3, Secrets Manager for OpenAI key) |
| T-2.5.10 | Output: `ecs_execution_role_arn`, `control_plane_task_role_arn`, `provider_worker_task_role_arn` |

### 2.6 ECS service module

| Task | Description |
|------|--------------|
| T-2.6.1 | Create ECS cluster |
| T-2.6.2 | Create ECR repository for Control Plane API |
| T-2.6.3 | Create ECR repository for Provider Worker |
| T-2.6.4 | Create ALB, target group (port 8080), listener (443) |
| T-2.6.5 | Create CloudWatch log groups for API and worker |
| T-2.6.6 | Define Control Plane API task definition (Fargate, CPU/memory, env vars, secrets from Secrets Manager) |
| T-2.6.7 | Define Provider Worker task definition |
| T-2.6.8 | Create ECS service for API (desired count, ALB integration, health check) |
| T-2.6.9 | Create ECS service for worker (desired count, no ALB) |
| T-2.6.10 | Output: `cluster_name`, `api_service_name`, `worker_service_name`, `alb_dns_name`, `ecr_api_repo_url`, `ecr_worker_repo_url` |

### 2.7 S3 module

| Task | Description |
|------|--------------|
| T-2.7.1 | Create prompts bucket (versioning, encryption) |
| T-2.7.2 | Create results bucket (versioning, encryption) |
| T-2.7.3 | Output: `prompts_bucket_name`, `prompts_bucket_arn`, `results_bucket_name`, `results_bucket_arn` |

### 2.8 Environment configs

| Task | Description |
|------|--------------|
| T-2.8.1 | `dev/main.tf`: Call all modules with dev-specific vars (small instance sizes, single NAT) |
| T-2.8.2 | `staging/main.tf`: Call modules with staging vars |
| T-2.8.3 | `prod/main.tf`: Call modules with prod vars (multi-AZ, larger instances) |
| T-2.8.4 | Create `dev.tfvars`, `staging.tfvars`, `prod.tfvars` for environment-specific values |

---

## 3. CI Workflow – Detailed Tasks

### 3.1 Workflow file

| Task | Description |
|------|--------------|
| T-3.1.1 | Create `.github/workflows/ci.yml` (or `.gitlab-ci.yml` / equivalent) |
| T-3.1.2 | Trigger: `pull_request` to `main` (and optionally `develop`) |

### 3.2 CI steps

| Step | Task | Description |
|------|------|--------------|
| 1 | T-3.2.1 | Checkout repository |
| 2 | T-3.2.2 | Setup .NET SDK (version matching project, e.g. 10.x) |
| 3 | T-3.2.3 | Cache NuGet packages |
| 4 | T-3.2.4 | Restore: `dotnet restore` for Control Plane solution |
| 5 | T-3.2.5 | Build: `dotnet build --no-restore` for Control Plane |
| 6 | T-3.2.6 | Restore: `dotnet restore` for Provider Worker solution |
| 7 | T-3.2.7 | Build: `dotnet build --no-restore` for Provider Worker |
| 8 | T-3.2.8 | Test Control Plane: `dotnet test --no-build` with coverage (optional) |
| 9 | T-3.2.9 | Test Provider Worker: `dotnet test --no-build` |
| 10 | T-3.2.10 | (Optional) Upload test results / coverage artifacts |
| 11 | T-3.2.11 | (Optional) Run `dotnet format` or security scan (e.g. `dotnet list package --vulnerable`) |

---

## 4. CD Workflows – Detailed Tasks

### 4.1 CD for dev (auto on merge to main)

| Task | Description |
|------|--------------|
| T-4.1.1 | Create `.github/workflows/cd-dev.yml` |
| T-4.1.2 | Trigger: `push` to `main` |
| T-4.1.3 | Use environment: `dev` (no approval gate) |

### 4.2 CD for staging / prod (manual)

| Task | Description |
|------|--------------|
| T-4.2.1 | Create `.github/workflows/cd-staging.yml` |
| T-4.2.2 | Trigger: `workflow_dispatch` or `release` |
| T-4.2.3 | Use environment: `staging` with required reviewers |
| T-4.2.4 | Create `.github/workflows/cd-prod.yml` |
| T-4.2.5 | Trigger: `workflow_dispatch` or `release` |
| T-4.2.6 | Use environment: `prod` with required reviewers |

### 4.3 CD pipeline steps (per environment)

| Step | Task | Description |
|------|------|--------------|
| 1 | T-4.3.1 | Checkout repo |
| 2 | T-4.3.2 | Configure AWS credentials (OIDC or stored credentials) |
| 3 | T-4.3.3 | Login to ECR: `aws ecr get-login-password` |
| 4 | T-4.3.4 | Build Control Plane API Docker image |
| 5 | T-4.3.5 | Tag image: `$ECR_URL/control-plane-api:$GIT_SHA` and `latest` |
| 6 | T-4.3.6 | Push Control Plane image to ECR |
| 7 | T-4.3.7 | Build Provider Worker Docker image |
| 8 | T-4.3.8 | Tag and push Provider Worker image |
| 9 | T-4.3.9 | Get current ECS task definition JSON |
| 10 | T-4.3.10 | Update `image` in task definition to new URI |
| 11 | T-4.3.11 | Register new task definition revision |
| 12 | T-4.3.12 | Update ECS API service: `aws ecs update-service --force-new-deployment` |
| 13 | T-4.3.13 | Update ECS worker service |
| 14 | T-4.3.14 | Wait for services to stabilize (or use `aws ecs wait services-stable`) |
| 15 | T-4.3.15 | Run smoke tests (see Section 7) |

---

## 5. First-Deploy – Step-by-Step Order

### Phase 1: Infrastructure

| Step | Task | Description |
|------|------|--------------|
| 1.1 | T-5.1.1 | `cd infra/terraform/environments/dev` |
| 1.2 | T-5.1.2 | `terraform init` |
| 1.3 | T-5.1.3 | `terraform plan -var-file=dev.tfvars` |
| 1.4 | T-5.1.4 | `terraform apply -var-file=dev.tfvars` (network first if split) |
| 1.5 | T-5.1.5 | Verify: DynamoDB table exists, GSI present, TTL enabled |
| 1.6 | T-5.1.6 | Verify: SQS queues exist, DLQ configured |
| 1.7 | T-5.1.7 | Verify: S3 buckets exist (prompts, results) |
| 1.8 | T-5.1.8 | Verify: ECR repos exist, ECS cluster created |

### Phase 2: Config & secrets

| Step | Task | Description |
|------|------|--------------|
| 2.1 | T-5.2.1 | Create secret in Secrets Manager: `prompt-gateway/dev/api-keys` (JSON array of keys) |
| 2.2 | T-5.2.2 | Create secret: `prompt-gateway/dev/openai-api-key` |
| 2.3 | T-5.2.3 | Create SSM params: DynamoDB table name, queue URLs, S3 bucket names |
| 2.4 | T-5.2.4 | Grant ECS task roles permission to read these secrets/params |

### Phase 3: Application deploy

| Step | Task | Description |
|------|------|--------------|
| 3.1 | T-5.3.1 | Build and push Control Plane API image to ECR |
| 3.2 | T-5.3.2 | Build and push Provider Worker image |
| 3.3 | T-5.3.3 | Create/update ECS task definitions with correct image URIs |
| 3.4 | T-5.3.4 | Deploy Control Plane API service |
| 3.5 | T-5.3.5 | Deploy Provider Worker service |
| 3.6 | T-5.3.6 | Verify tasks are running (ECS console or CLI) |

### Phase 4: Smoke tests

| Step | Task | Description |
|------|------|--------------|
| 4.1 | T-5.4.1 | `curl https://<alb>/health` → 200 |
| 4.2 | T-5.4.2 | `curl https://<alb>/ready` → 200 (DynamoDB, SQS reachable) |
| 4.3 | T-5.4.3 | `POST /jobs` with valid payload + `X-API-Key` → 201, job_id returned |
| 4.4 | T-5.4.4 | `GET /jobs/{job_id}` → job status |
| 4.5 | T-5.4.5 | Wait for job completion, `GET /jobs/{job_id}/result` → 200 |

---

## 6. Secrets & Configuration – Detailed Tasks

| Task | Description |
|------|--------------|
| T-6.1 | Create Secrets Manager secret for API keys (format: `["key1","key2"]`) |
| T-6.2 | Create secret for OpenAI API key |
| T-6.3 | Create SSM parameter: `DynamoDb__TableName` |
| T-6.4 | Create SSM parameter: `DynamoDb__JobListIndexName` |
| T-6.5 | Create SSM parameter: `Sqs__DispatchQueueUrl` |
| T-6.6 | Create SSM parameter: `Sqs__ResultQueueUrl` |
| T-6.7 | Create SSM parameter: `S3__PromptsBucket`, `S3__ResultsBucket` (if used) |
| T-6.8 | Wire ECS task definition `secrets` block to Secrets Manager ARNs |
| T-6.9 | Wire `environment` or `secrets` for SSM params (or use custom entrypoint to fetch at startup) |
| T-6.10 | Document required env vars: `ApiSecurity__ApiKeys__0`, `OpenAI__ApiKey`, etc. |

---

## 7. Smoke Test Script – Detailed Tasks

| Task | Description |
|------|--------------|
| T-7.1 | Create `scripts/smoke-test.sh` (or `.ps1` for Windows) |
| T-7.2 | Accept args: `BASE_URL`, `API_KEY` |
| T-7.3 | Call `GET /health`, exit 1 if not 200 |
| T-7.4 | Call `GET /ready`, exit 1 if not 200 |
| T-7.5 | Call `POST /jobs` with minimal valid payload, capture `job_id` |
| T-7.6 | Poll `GET /jobs/{job_id}` until status is `Completed` or `Failed` (timeout 60s) |
| T-7.7 | If Completed: `GET /jobs/{job_id}/result`, assert 200 |
| T-7.8 | If Failed: log and exit 1 |
| T-7.9 | Integrate script into CD workflow (call after ECS deploy) |

---

## 8. Rollback & Resilience – Detailed Tasks

| Task | Description |
|------|--------------|
| T-8.1 | Document ECS rollback: `aws ecs update-service --task-definition <previous-revision>` |
| T-8.2 | Create CloudWatch alarm: API 5xx rate > threshold |
| T-8.3 | Create CloudWatch alarm: ECS CPU/memory utilization |
| T-8.4 | Create CloudWatch alarm: SQS DLQ message count > 0 |
| T-8.5 | Create CloudWatch alarm: DynamoDB throttles |
| T-8.6 | Wire alarms to SNS topic (email or PagerDuty) |
| T-8.7 | Ensure `/health` (liveness) and `/ready` (readiness) are used in ECS health checks |

---

## 9. Master Task Checklist

### IaC
- [x] T-2.1.1 – T-2.1.5: Repository structure
- [x] T-2.2.1 – T-2.2.10: Network module
- [x] T-2.3.1 – T-2.3.6: DynamoDB module
- [x] T-2.4.1 – T-2.4.6: SQS module
- [x] T-2.5.1 – T-2.5.10: IAM module
- [x] T-2.6.1 – T-2.6.10: ECS service module
- [x] T-2.7.1 – T-2.7.3: S3 module
- [x] T-2.8.1 – T-2.8.4: Environment configs

### CI
- [ ] T-3.1.1 – T-3.1.2: Workflow file
- [ ] T-3.2.1 – T-3.2.11: CI steps

### CD
- [ ] T-4.1.1 – T-4.1.3: CD dev
- [ ] T-4.2.1 – T-4.2.6: CD staging/prod
- [ ] T-4.3.1 – T-4.3.15: CD pipeline steps

### First deploy
- [ ] T-5.1.1 – T-5.1.8: Phase 1 – Infrastructure
- [ ] T-5.2.1 – T-5.2.4: Phase 2 – Config & secrets
- [ ] T-5.3.1 – T-5.3.6: Phase 3 – Application deploy
- [ ] T-5.4.1 – T-5.4.5: Phase 4 – Smoke tests

### Secrets & config
- [ ] T-6.1 – T-6.10

### Smoke test script
- [ ] T-7.1 – T-7.9

### Rollback & resilience
- [ ] T-8.1 – T-8.7
