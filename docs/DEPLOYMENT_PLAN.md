# Prompt Gateway – Deployment & CI/CD Plan

This document describes the target architecture, infrastructure-as-code layout, CI/CD workflows, and first-deploy procedures for the Prompt Gateway Control Plane and Provider Worker. Each section is broken into detailed steps and tasks.

> **Document status: Historical plan record**
>
> This file captures the original deployment and CI/CD planning work. It is no longer the primary source of truth for the current implementation state.
>
> Use these docs first for the current implemented system:
>
> - `docs/README.md`
> - `docs/IMPLEMENTATION_BACKLOG.md`
> - `infra/terraform/README.md`
> - `Prompt Gateway – Control Plane /src/ControlPlane.Api/README.md`
> - `Prompt Gateway Provider - OpenAI/README.md`
>
> Historical note:
> the older statement that Terraform was only skeleton code is no longer accurate for the current repository state. Keep this document as planning history unless you are intentionally updating the historical plan.

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
| T-2.3.6 | Output: `table_name`, `table_arn`, `gsi_name`, `dedupe_table_name`, `dedupe_table_arn` |

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
| T-2.5.9 | Create IAM role for Provider Worker task (SQS, DynamoDB dedupe table, S3, Secrets Manager for OpenAI key) |
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
| 5 | T-4.3.5 | Tag image with an immutable build tag, e.g. `$ECR_URL/control-plane-api:$GIT_SHA` |
| 6 | T-4.3.6 | Push Control Plane image to ECR |
| 7 | T-4.3.7 | Build Provider Worker Docker image |
| 8 | T-4.3.8 | Tag and push Provider Worker image |
| 9 | T-4.3.9 | Get current ECS task definition JSON |
| 10 | T-4.3.10 | Update `image` in task definition to new URI |
| 11 | T-4.3.11 | Register new task definition revision |
| 12 | T-4.3.12 | Update ECS API service: `aws ecs update-service --force-new-deployment` |
| 13 | T-4.3.13 | Update ECS worker service |
| 14 | T-4.3.14 | Wait for services to stabilize (or use `aws ecs wait services-stable`) |
| 15 | T-4.3.15 | Run smoke tests (see Section 7). For staging/prod: set `HEALTH_CHECK_BASE_URL` env var (e.g. `https://api.example.com`) to use full SSL verification; otherwise ALB DNS is used with `-k` (skips cert verification). |

---

## 5. First-Deploy – Step-by-Step Order

### Phase 1: Infrastructure

| Step | Task | Description |
|------|------|--------------|
| 1.1 | T-5.1.1 | `cd infra/terraform/environments/dev` |
| 1.2 | T-5.1.2 | `terraform init` (run `./scripts/bootstrap-terraform-backend.sh dev` first if backend not created) |
| 1.3 | T-5.1.3 | `terraform plan -var-file=dev.tfvars` |
| 1.4 | T-5.1.4 | `terraform apply -var-file=dev.tfvars` (network first if split) |
| 1.5 | T-5.1.5 | Verify: DynamoDB table exists, GSI present, TTL enabled |
| 1.6 | T-5.1.6 | Verify: SQS queues exist, DLQ configured |
| 1.7 | T-5.1.7 | Verify: S3 buckets exist (prompts, results) |
| 1.8 | T-5.1.8 | Verify: ECR repos exist, ECS cluster created |

**Automation:** Run `./scripts/first-deploy-phase1.sh` to execute T-5.1.1 – T-5.1.8. Use `--plan-only` to plan without apply, `--skip-verify` to skip post-apply verification.

### Phase 2: Config & secrets

| Step | Task | Description |
|------|------|--------------|
| 2.1 | T-5.2.1 | Create API keys (JSON array). **Dev:** SSM Parameter Store SecureString `prompt-gateway/dev/api-keys` (no Secrets Manager cost). **Staging/prod:** Secrets Manager `prompt-gateway/<env>/api-keys`. |
| 2.2 | T-5.2.2 | Create OpenAI key. **Dev:** SSM SecureString `prompt-gateway/dev/openai-api-key`. **Staging/prod:** Secrets Manager `prompt-gateway/<env>/openai-api-key`. |
| 2.3 | T-5.2.3 | Create SSM params: DynamoDB table name, queue URLs, S3 bucket names |
| 2.4 | T-5.2.4 | Grant ECS task roles permission to read these secrets/params (SSM for dev secrets; Secrets Manager for staging/prod) |

**Automation:** Run `./scripts/first-deploy-phase2.sh` to execute T-5.2.1 – T-5.2.4. For **dev**, API keys and OpenAI key are stored in Parameter Store (SecureString) to avoid Secrets Manager per-secret cost; staging and prod use Secrets Manager. Optional env vars: `API_KEYS_JSON` (e.g. `'["key1","key2"]'`), `OPENAI_API_KEY`. If `API_KEYS_JSON` is not set and Bitwarden CLI (`bw`) is installed and unlocked, the script will generate a new API key and create a secure note in Bitwarden (e.g. "Prompt Gateway dev API keys") before writing to AWS. Use `--skip-secrets` or `--skip-ssm` to run only part of the phase. Requires Phase 1 complete and `jq` installed.

### Phase 3: Application deploy

| Step | Task | Description |
|------|------|--------------|
| 3.1 | T-5.3.1 | Build and push Control Plane API image to ECR |
| 3.2 | T-5.3.2 | Build and push Provider Worker image |
| 3.3 | T-5.3.3 | Create/update ECS task definitions with correct image URIs |
| 3.4 | T-5.3.4 | Deploy Control Plane API service |
| 3.5 | T-5.3.5 | Deploy Provider Worker service |
| 3.6 | T-5.3.6 | Verify tasks are running (ECS console or CLI) |

**Automation:** Run `./scripts/first-deploy-phase3.sh --processing-mode ecs|lambda` to execute Phase 3 for the selected runtime. `ecs` keeps queue processing on ECS and turns Lambda processing off. `lambda` packages and enables Lambda processing while scaling ECS queue workers down. Requires Phase 1 and Phase 2 complete, Docker and `jq` installed. Optional env vars: `ENV` (dev | staging | prod), `IMAGE_TAG` (default: current git SHA, or UTC timestamp fallback), `AWS_REGION` (default: us-east-1). Use `--build-only` to only build and push images without deploying; use `--skip-verify` to skip the final runtime verification. Terraform ECS task definitions now take explicit `api_image_tag` and `worker_image_tag` inputs and default to a `bootstrap` placeholder until a real build tag is promoted.

### Phase 4: Smoke tests

| Step | Task | Description |
|------|------|--------------|
| 4.1 | T-5.4.1 | `curl https://<alb>/health` → 200 |
| 4.2 | T-5.4.2 | `curl https://<alb>/ready` → 200 (DynamoDB, SQS reachable) |
| 4.3 | T-5.4.3 | `POST /jobs` with valid payload + `X-API-Key` → 202 accepted (or legacy 200/201), job_id returned |
| 4.4 | T-5.4.4 | `GET /jobs/{job_id}` → job status |
| 4.5 | T-5.4.5 | Wait for job completion, `GET /jobs/{job_id}/result` → 200 |

**Automation:** Run `./scripts/first-deploy-phase4.sh` to execute T-5.4.1 – T-5.4.5 after either ECS mode or Lambda mode is deployed. Uses `scripts/smoke-test.sh`; resolves BASE_URL from API Gateway by default, or from the environment’s ALB when `HTTP_EDGE_MODE=ecs`, unless `BASE_URL` / `HEALTH_CHECK_BASE_URL` is set explicitly. The script uploads a smoke prompt fixture to S3 by default and resolves the API key from SSM (dev) or Secrets Manager (staging/prod). Optional env vars: `ENV`, `HTTP_EDGE_MODE`, `BASE_URL`, `API_KEY`, `AWS_REGION`, `SMOKE_INPUT_REF`, `SMOKE_PROMPT_BUCKET`, `SMOKE_PROMPT_TEXT`, `SMOKE_SKIP_PROMPT_UPLOAD=true`. Use `--insecure` for HTTPS with self-signed or ALB hostname when not using a custom domain. Requires Phase 1–3 complete and `jq` installed. On failure, the smoke test now prints the final `GET /jobs/{jobId}` payload and `GET /jobs/{jobId}/events` timeline to speed up diagnosis.

**Verification gate:** `./scripts/set-processing-mode.sh --mode ecs|lambda --verify-only --run-smoke-test` first checks that the expected runtime is active and the alternate runtime is disabled, then runs the end-to-end smoke test in the selected mode. This is the recommended promotion gate for dev and staging.

**Promotion evidence**

- Dev:
  - `set-processing-mode.sh --verify-only --run-smoke-test` passes
  - no active CloudWatch alarms for API 5xx, Lambda errors/throttles, queue age/backlog, or DLQ depth
  - smoke test evidence is captured in the deployment log
- Staging:
  - repeat the dev gate after deploying the same image tag / Lambda artifact set
  - capture a successful smoke-test run and confirm no backlog alarms are active for at least one scheduler interval
- Prod:
  - promote only after staging evidence exists for the same build set
  - keep the prior deploy identifiers and the rollback command ready before switching modes

**Rollback gate**

- If `/ready` fails, the smoke test fails, or any Lambda/queue alarm trips during cutover, stop the promotion.
- Roll back by restoring the previous Terraform apply inputs or switching the environment back with `./scripts/set-processing-mode.sh --mode ecs`.
- Re-run `--verify-only --run-smoke-test` after rollback and keep the failure evidence with the deployment notes.

For Lambda-mode promotion while keeping ECS mode as a fallback, use `./scripts/promote-lambda-mode.sh staging` and then `./scripts/promote-lambda-mode.sh prod --check-staging`.

---

## 6. Secrets & Configuration – Detailed Tasks

| Task | Description |
|------|--------------|
| T-6.1 | Create Secrets Manager secret for API keys (format: `["key1","key2"]`) |
| T-6.2 | Create secret for OpenAI API key |
| T-6.3 | Create SSM parameter: `DynamoDb__TableName` |
| T-6.4 | Create SSM parameter: `DynamoDb__JobListIndexName` |
| T-6.5 | Create SSM parameter: `Sqs__DispatchQueueUrl` |
| T-6.6 | Create SSM parameter: `Sqs__ResultQueueUrl` (Control Plane receives job results from this queue) |
| T-6.7 | Create SSM parameter: `S3__PromptsBucket`, `S3__ResultsBucket` (if used) |
| T-6.8 | Wire ECS task definition `secrets` block to Secrets Manager ARNs |
| T-6.9 | Wire `environment` or `secrets` for SSM params (or use custom entrypoint to fetch at startup) |
| T-6.10 | Document required env vars: `ApiSecurity__ApiKeys__0` (or `ApiSecurity__ApiKey` as single key/JSON array), `ProviderWorker__OpenAi__ApiKey`, etc. |

### Implementation notes

- **T-6.1, T-6.2:** Implemented by `scripts/first-deploy-phase2.sh` (Phase 2). **Dev:** API keys and OpenAI key are stored in SSM Parameter Store (SecureString) at `/prompt-gateway/{env}/api-keys` and `/prompt-gateway/{env}/openai-api-key` to avoid Secrets Manager per-secret cost. **Staging/prod:** Secrets Manager secrets `prompt-gateway/{env}/api-keys` and `prompt-gateway/{env}/openai-api-key` are created/updated. Optional env vars for the script: `API_KEYS_JSON`, `OPENAI_API_KEY`; if unset and Bitwarden CLI is unlocked, the script can generate and store a key.
- **T-6.3 – T-6.7:** Phase 2 creates SSM parameters under `/prompt-gateway/{env}/`: `dynamodb-table-name`, `dynamodb-gsi-name`, `dispatch-queue-url`, `result-queue-url`, `prompts-bucket`, `results-bucket`. These are used by scripts (e.g. Phase 4 smoke test) and operators. ECS task definitions **do not** read these SSM params for queue/table config; Terraform passes queue URLs and table names from module outputs into the task definition `environment` block.
- **T-6.8:** ECS task definitions in `infra/terraform/modules/ecs-service/main.tf` use a `secrets` block. Control Plane: `ApiSecurity__ApiKey` from Secrets Manager (staging/prod) or from SSM parameter ARN (dev). Provider Worker: `ProviderWorker__OpenAi__ApiKey` from the same source. IAM execution roles have `secretsmanager:GetSecretValue` and (for dev) SSM `GetParameter` on the relevant ARNs.
- **T-6.9:** Task definition `environment` block sets `AwsQueue__DispatchQueueUrl`, `AwsQueue__ResultQueueUrl`, `AwsStorage__TableName`, `AwsStorage__JobListIndexName` from Terraform variables (module outputs). Secrets are injected via `valueFrom` (Secrets Manager or SSM). No custom entrypoint is required.

### T-6.10: Required environment variables and secrets

| Component | Name | Type | Description |
|-----------|------|------|-------------|
| **Control Plane API** | `ApiSecurity__ApiKey` | Secret | JSON array of API keys, e.g. `["key1","key2"]`. In ECS: from Secrets Manager or SSM (dev). At least one key required. |
| | `AwsQueue__DispatchQueueUrl` | Env | SQS dispatch queue URL (from Terraform). |
| | `AwsQueue__ResultQueueUrl` | Env | SQS result queue URL (from Terraform). |
| | `AwsStorage__TableName` | Env | DynamoDB table name (from Terraform). |
| | `AwsStorage__JobListIndexName` | Env | GSI name, e.g. `JobListIndex` (from Terraform). |
| | `AwsStorage__*` (optional) | Env | `DeduplicationTtlDays`, `OutboxTerminalTtlDays`, `EventTtlDays`, `ResultTtlDays` – have defaults. |
| **Provider Worker** | `ProviderWorker__OpenAi__ApiKey` | Secret | OpenAI API key. In ECS: from Secrets Manager or SSM (dev). |
| | `ProviderWorker__InputQueueUrl` | Env | SQS dispatch queue URL (from Terraform). |
| | `ProviderWorker__OutputQueueUrl` | Env | SQS result queue URL (from Terraform). |
| | `ProviderWorker__DedupeTableName` | Env | DynamoDB dedupe table name (from Terraform). |
| | `ProviderWorker__PromptBucket` | Env | (Optional) S3 prompts bucket name. |
| | `ProviderWorker__ResultBucket` | Env | (Optional) S3 results bucket name. |

**Client authentication:** Send `X-API-Key: <key>` on requests to protected Control Plane endpoints. Keys are validated against the configured `ApiSecurity__ApiKey` value (JSON array or single key).

---

## 7. Smoke Test Script – Detailed Tasks

| Task | Description |
|------|--------------|
| T-7.1 | Create `scripts/smoke-test.sh` (or `.ps1` for Windows) |
| T-7.2 | Accept args: `BASE_URL`, `API_KEY` |
| T-7.3 | Call `GET /health`, exit 1 if not 200 |
| T-7.4 | Call `GET /ready`, exit 1 if not 200 |
| T-7.5 | Call `POST /jobs` with minimal valid payload, capture `job_id` and resume if the response indicates follow-up dispatch is required |
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

### T-8.1: ECS rollback

To roll back the Control Plane API or Provider Worker to a previous task definition revision:

1. List recent task definition revisions:
   ```bash
   aws ecs list-task-definitions --family prompt-gateway-<ENV>-control-plane-api --sort DESC --max-items 5 --region <REGION>
   aws ecs list-task-definitions --family prompt-gateway-<ENV>-provider-worker --sort DESC --max-items 5 --region <REGION>
   ```
2. Update the service to use the previous revision (replace `<ENV>`, `<REVISION>`, and `<REGION>`):
   ```bash
   aws ecs update-service --cluster prompt-gateway-<ENV> --service control-plane-api \
     --task-definition prompt-gateway-<ENV>-control-plane-api:<REVISION> \
     --force-new-deployment --region <REGION> --no-cli-pager
   ```
   For the worker:
   ```bash
   aws ecs update-service --cluster prompt-gateway-<ENV> --service provider-worker \
     --task-definition prompt-gateway-<ENV>-provider-worker:<REVISION> \
     --force-new-deployment --region <REGION> --no-cli-pager
   ```
3. Wait for the service to stabilize:
   ```bash
   aws ecs wait services-stable --cluster prompt-gateway-<ENV> --services control-plane-api provider-worker --region <REGION>
   ```

**Example (dev):** Roll back the API to revision 3:  
`aws ecs update-service --cluster prompt-gateway-dev --service control-plane-api --task-definition prompt-gateway-dev-control-plane-api:3 --force-new-deployment --region us-east-1 --no-cli-pager`

### T-8.2 – T-8.6: CloudWatch alarms and SNS

Implemented by the **monitoring** Terraform module (`infra/terraform/modules/monitoring/`):

- **T-8.2:** Alarm `api_5xx` – ALB target group metric `HTTPCode_Target_5XX_Count`, threshold configurable.
- **T-8.3:** Alarms `ecs_api_cpu` and `ecs_api_memory` – ECS service CPU and memory utilization.
- **T-8.4:** Alarm `sqs_dlq_messages` – SQS DLQ `ApproximateNumberOfMessagesVisible` > 0.
- **T-8.5:** Alarm `dynamodb_throttles` – DynamoDB throttle/error metrics for the main table.
- **T-8.6:** All alarms notify an SNS topic. Optional email subscription via variable `alarm_email`; add PagerDuty or other subscriptions to the topic as needed.

### T-8.7: Health and readiness checks

- **Liveness:** ECS **container** health check uses `GET /health`. If it fails, ECS stops the task and replaces it.
- **Readiness:** **ALB target group** health check uses `GET /ready`. Traffic is only sent to tasks that pass (DynamoDB and SQS verified).

---

## 9. Troubleshooting

### ECS: "secret was marked for deletion" (InvalidRequestException) in dev

**Cause:** The running ECS task definition revision was created by Phase 3 or CD (describe → update image → register). That revision kept the **secrets** from the previous revision, which pointed at Secrets Manager. In dev, API keys and OpenAI key are stored in **SSM Parameter Store**, not Secrets Manager. If the secret in Secrets Manager was deleted (or marked for deletion), ECS fails when pulling it.

**Fix:**

1. Re-apply Terraform for dev so it registers a new task definition revision that uses SSM:
   ```bash
   cd infra/terraform/environments/dev
   terraform apply -var-file=dev.tfvars
   ```
2. Force the ECS services to use the new revision:
   ```bash
   aws ecs update-service --cluster prompt-gateway-dev --service control-plane-api --force-new-deployment --region us-east-1 --no-cli-pager
   aws ecs update-service --cluster prompt-gateway-dev --service provider-worker --force-new-deployment --region us-east-1 --no-cli-pager
   ```

After this, the active task definition will use SSM (`/prompt-gateway/dev/api-keys` and `/prompt-gateway/dev/openai-api-key`). Ensure Phase 2 has created those SSM parameters.

### ECS: control-plane-api "failed container health checks"

**Cause:** The ECS task definition runs `curl -f http://localhost:8080/health` for the container health check. The Microsoft `aspnet` runtime image does **not** include `curl`, so the check fails and ECS marks the task unhealthy.

**Fix:** The Control Plane API Dockerfile installs `curl` in the final stage so the health check succeeds. Rebuild and push the image, then force a new deployment:

```bash
ENV=dev ./scripts/first-deploy-phase3.sh --processing-mode ecs
```

If health checks still fail after curl is added, check CloudWatch Logs for the task: the app may be crashing on startup (e.g. missing config or AWS dependency). Increase the task definition’s health check `startPeriod` (e.g. to 90s) if the app needs longer to start.

### /ready returns 503

**Meaning:** The readiness endpoint runs the **AWS dependencies** health check (DynamoDB table + SQS dispatch and result queues). A 503 means that check is unhealthy.

**How to see the reason:**

1. **Response body** – The API returns a JSON body describing the failure. Run:
   ```bash
   curl -s -H "X-API-Key: YOUR_API_KEY" "http://<ALB_DNS>/ready"
   ```
   Or re-run the smoke test; it now prints the response body when `/ready` fails.

2. **Typical causes:**
   - **Missing or wrong env in the running task** – The Control Plane API task must have `AwsStorage__TableName`, `AwsQueue__DispatchQueueUrl`, and `AwsQueue__ResultQueueUrl` set (from Terraform when the task definition was created). If you only updated the task definition image/secrets in CD or Phase 3 and the **revision** you’re running was registered without these env vars, they will be missing. **Fix:** Redeploy using a task definition that includes the environment block (e.g. run `terraform apply` for the ECS module, then update the service to use the Terraform-managed task definition, or re-register a new revision that includes these env vars).
   - **IAM** – The task role needs permission to `dynamodb:DescribeTable` and `sqs:GetQueueAttributes` on the table and both queues. Check the IAM module and task role.
   - **Wrong URLs/table name** – Verify the task’s env values match the Terraform outputs (e.g. `terraform output -json` in the dev environment).

3. **Check the live task definition:**
   ```bash
   aws ecs describe-task-definition --task-definition prompt-gateway-dev-control-plane-api \
     --query 'taskDefinition.containerDefinitions[0].environment'
   ```
   Ensure `AwsQueue__DispatchQueueUrl`, `AwsQueue__ResultQueueUrl`, and `AwsStorage__TableName` are present and correct.

---

## 10. Master Task Checklist

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
- [x] T-3.1.1 – T-3.1.2: Workflow file
- [x] T-3.2.1 – T-3.2.11: CI steps

### CD
- [x] T-4.1.1 – T-4.1.3: CD dev
- [x] T-4.2.1 – T-4.2.6: CD staging/prod
- [x] T-4.3.1 – T-4.3.15: CD pipeline steps

### First deploy
- [x] T-5.1.1 – T-5.1.8: Phase 1 – Infrastructure (script: `scripts/first-deploy-phase1.sh`)
- [x] T-5.2.1 – T-5.2.4: Phase 2 – Config & secrets
- [x] T-5.3.1 – T-5.3.6: Phase 3 – Application deploy (script: `scripts/first-deploy-phase3.sh`)
- [x] T-5.4.1 – T-5.4.5: Phase 4 – Smoke tests (script: `scripts/first-deploy-phase4.sh`)

### Secrets & config
- [x] T-6.1 – T-6.10 (Phase 2 script + Terraform ECS module; see §6 implementation notes and T-6.10 table)

### Smoke test script
- [x] T-7.1 – T-7.9

### Rollback & resilience
- [x] T-8.1 – T-8.7 (rollback doc §8; monitoring module + dev wired; ALB uses /ready, container /health)
