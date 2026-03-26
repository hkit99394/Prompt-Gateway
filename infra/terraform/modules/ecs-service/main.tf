# ECS service module - cluster, task definitions, services
# Implementation: T-2.6.x

data "aws_caller_identity" "current" {}
data "aws_region" "current" {}

locals {
  account_id            = data.aws_caller_identity.current.account_id
  region                = data.aws_region.current.name
  api_image             = "${local.account_id}.dkr.ecr.${local.region}.amazonaws.com/prompt-gateway-${var.environment}-control-plane-api:latest"
  worker_image          = "${local.account_id}.dkr.ecr.${local.region}.amazonaws.com/prompt-gateway-${var.environment}-provider-worker:latest"
  api_keys_secret_arn   = "arn:aws:secretsmanager:${local.region}:${local.account_id}:secret:prompt-gateway/${var.environment}/api-keys"
  openai_key_secret_arn = "arn:aws:secretsmanager:${local.region}:${local.account_id}:secret:prompt-gateway/${var.environment}/openai-api-key"
  # Dev uses SSM Parameter Store (no Secrets Manager cost); staging/prod use Secrets Manager
  api_keys_value_from   = lower(var.environment) == "dev" ? "arn:aws:ssm:${local.region}:${local.account_id}:parameter/prompt-gateway/${var.environment}/api-keys" : local.api_keys_secret_arn
  openai_key_value_from = lower(var.environment) == "dev" ? "arn:aws:ssm:${local.region}:${local.account_id}:parameter/prompt-gateway/${var.environment}/openai-api-key" : local.openai_key_secret_arn
  use_https             = var.certificate_arn != ""
}

# T-2.6.1: ECS cluster
resource "aws_ecs_cluster" "main" {
  name = "prompt-gateway-${var.environment}"

  setting {
    name  = "containerInsights"
    value = "disabled"
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}"
    Environment = var.environment
  }

  lifecycle {
    precondition {
      condition     = var.environment == "dev" || var.certificate_arn != ""
      error_message = "certificate_arn must be set for staging and prod environments. Dev may use HTTP-only by omitting it."
    }
  }
}

# T-2.6.2: ECR repository for Control Plane API
resource "aws_ecr_repository" "control_plane_api" {
  name                 = "prompt-gateway-${var.environment}-control-plane-api"
  image_tag_mutability = "MUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-control-plane-api"
    Environment = var.environment
  }
}

# T-2.6.3: ECR repository for Provider Worker
resource "aws_ecr_repository" "provider_worker" {
  name                 = "prompt-gateway-${var.environment}-provider-worker"
  image_tag_mutability = "MUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-provider-worker"
    Environment = var.environment
  }
}

# T-2.6.4: ALB
resource "aws_lb" "main" {
  name               = "prompt-gateway-${var.environment}"
  internal           = false
  load_balancer_type = "application"
  security_groups    = [var.alb_security_group_id]
  subnets            = var.public_subnet_ids

  tags = {
    Name        = "prompt-gateway-${var.environment}"
    Environment = var.environment
  }
}

resource "aws_lb_target_group" "api" {
  name        = "pg-${var.environment}-api"
  port        = 8080
  protocol    = "HTTP"
  vpc_id      = var.vpc_id
  target_type = "ip"

  health_check {
    enabled             = true
    path                = "/ready"
    protocol            = "HTTP"
    healthy_threshold   = 2
    unhealthy_threshold = 3
    timeout             = 5
    interval            = 30
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-api"
    Environment = var.environment
  }
}

resource "aws_lb_listener" "http_forward" {
  count = local.use_https ? 0 : 1

  load_balancer_arn = aws_lb.main.arn
  port              = "80"
  protocol          = "HTTP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }
}

resource "aws_lb_listener" "http_redirect" {
  count = local.use_https ? 1 : 0

  load_balancer_arn = aws_lb.main.arn
  port              = "80"
  protocol          = "HTTP"

  default_action {
    type = "redirect"

    redirect {
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }
}

resource "aws_lb_listener" "https" {
  count = local.use_https ? 1 : 0

  load_balancer_arn = aws_lb.main.arn
  port              = "443"
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = var.certificate_arn

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }
}

# T-2.6.5: CloudWatch log groups
resource "aws_cloudwatch_log_group" "api" {
  name              = "/ecs/prompt-gateway-${var.environment}/control-plane-api"
  retention_in_days = 14

  tags = {
    Name        = "prompt-gateway-${var.environment}-api"
    Environment = var.environment
  }
}

resource "aws_cloudwatch_log_group" "worker" {
  name              = "/ecs/prompt-gateway-${var.environment}/provider-worker"
  retention_in_days = 14

  tags = {
    Name        = "prompt-gateway-${var.environment}-worker"
    Environment = var.environment
  }
}

# T-2.6.6: Control Plane API task definition
resource "aws_ecs_task_definition" "control_plane_api" {
  family                   = "prompt-gateway-${var.environment}-control-plane-api"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = var.api_cpu
  memory                   = var.api_memory
  execution_role_arn       = var.ecs_execution_control_plane_role_arn
  task_role_arn            = var.control_plane_task_role_arn

  container_definitions = jsonencode([
    {
      name      = "control-plane-api"
      image     = local.api_image
      essential = true

      portMappings = [
        {
          containerPort = 8080
          protocol      = "tcp"
          appProtocol   = "http"
        }
      ]

      environment = [
        { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
        { name = "ASPNETCORE_URLS", value = "http://+:8080" },
        { name = "AwsQueue__DispatchQueueUrl", value = var.dispatch_queue_url },
        { name = "AwsQueue__ResultQueueUrl", value = var.result_queue_url },
        { name = "AwsStorage__TableName", value = var.dynamodb_table_name },
        { name = "AwsStorage__JobListIndexName", value = var.dynamodb_gsi_name },
        { name = "HostedWorkers__EnableOutboxWorker", value = var.disable_api_hosted_workers ? "false" : "true" },
        { name = "HostedWorkers__EnableResultQueueWorker", value = var.disable_api_hosted_workers ? "false" : "true" }
      ]

      secrets = [
        {
          name      = "ApiSecurity__ApiKey"
          valueFrom = local.api_keys_value_from
        }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.api.name
          "awslogs-region"        = local.region
          "awslogs-stream-prefix" = "ecs"
        }
      }

      healthCheck = {
        command     = ["CMD-SHELL", "curl -f http://localhost:8080/health || exit 1"]
        interval    = 30
        timeout     = 5
        retries     = 3
        startPeriod = 90
      }
    }
  ])

  tags = {
    Name        = "prompt-gateway-${var.environment}-control-plane-api"
    Environment = var.environment
  }
}

# T-2.6.7: Provider Worker task definition
resource "aws_ecs_task_definition" "provider_worker" {
  family                   = "prompt-gateway-${var.environment}-provider-worker"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = var.worker_cpu
  memory                   = var.worker_memory
  execution_role_arn       = var.ecs_execution_provider_worker_role_arn
  task_role_arn            = var.provider_worker_task_role_arn

  container_definitions = jsonencode([
    {
      name      = "provider-worker"
      image     = local.worker_image
      essential = true

      environment = concat(
        [
          { name = "DOTNET_ENVIRONMENT", value = "Production" },
          { name = "ProviderWorker__InputQueueUrl", value = var.dispatch_queue_url },
          { name = "ProviderWorker__OutputQueueUrl", value = var.result_queue_url },
          { name = "ProviderWorker__DedupeTableName", value = var.worker_dedupe_table_name }
        ],
        var.prompts_bucket_name != "" ? [{ name = "ProviderWorker__PromptBucket", value = var.prompts_bucket_name }] : [],
        var.results_bucket_name != "" ? [{ name = "ProviderWorker__ResultBucket", value = var.results_bucket_name }] : []
      )

      secrets = [
        {
          name      = "ProviderWorker__OpenAi__ApiKey"
          valueFrom = local.openai_key_value_from
        }
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.worker.name
          "awslogs-region"        = local.region
          "awslogs-stream-prefix" = "ecs"
        }
      }
    }
  ])

  tags = {
    Name        = "prompt-gateway-${var.environment}-provider-worker"
    Environment = var.environment
  }
}

# T-2.6.8: ECS service for API
resource "aws_ecs_service" "api" {
  name            = "control-plane-api"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.control_plane_api.arn
  desired_count   = var.api_desired_count
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [var.ecs_api_security_group_id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "control-plane-api"
    container_port   = 8080
  }

  deployment_controller {
    type = "ECS"
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-control-plane-api"
    Environment = var.environment
  }
}

# T-2.6.9: ECS service for worker
resource "aws_ecs_service" "worker" {
  name            = "provider-worker"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.provider_worker.arn
  desired_count   = var.disable_provider_worker_service ? 0 : var.worker_desired_count
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [var.ecs_worker_security_group_id]
    assign_public_ip = false
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-provider-worker"
    Environment = var.environment
  }
}
