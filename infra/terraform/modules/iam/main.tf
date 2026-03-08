# IAM module - ECS task roles and policies
# Implementation: T-2.5.x

data "aws_caller_identity" "current" {}
data "aws_region" "current" {}

locals {
  account_id   = data.aws_caller_identity.current.account_id
  region       = data.aws_region.current.name
  # Least privilege: Control Plane gets API keys only; Provider Worker gets OpenAI key only
  control_plane_secrets_arns = [
    "arn:aws:secretsmanager:${local.region}:${local.account_id}:secret:prompt-gateway/${var.environment}/api-keys*"
  ]
  provider_worker_secrets_arns = [
    "arn:aws:secretsmanager:${local.region}:${local.account_id}:secret:prompt-gateway/${var.environment}/openai-api-key*"
  ]
  ssm_prefix = "arn:aws:ssm:${local.region}:${local.account_id}:parameter/prompt-gateway/${var.environment}/*"
  # Dev stores api-keys and openai-api-key in SSM; execution role needs GetParameter to inject them
  ssm_api_keys_param_arn   = "arn:aws:ssm:${local.region}:${local.account_id}:parameter/prompt-gateway/${var.environment}/api-keys"
  ssm_openai_key_param_arn = "arn:aws:ssm:${local.region}:${local.account_id}:parameter/prompt-gateway/${var.environment}/openai-api-key"
}

# T-2.5.1: ECS task execution roles (pull images, write logs, fetch secrets at startup)
# Split by task type for least privilege: each role only accesses its own secrets
resource "aws_iam_role" "ecs_execution_control_plane" {
  name = "prompt-gateway-${var.environment}-ecs-execution-api"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
      }
    ]
  })

  tags = {
    Name        = "prompt-gateway-${var.environment}-ecs-execution-api"
    Environment = var.environment
  }
}

resource "aws_iam_role" "ecs_execution_provider_worker" {
  name = "prompt-gateway-${var.environment}-ecs-execution-worker"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
      }
    ]
  })

  tags = {
    Name        = "prompt-gateway-${var.environment}-ecs-execution-worker"
    Environment = var.environment
  }
}

# T-2.5.2: Attach AmazonECSTaskExecutionRolePolicy (ECR, CloudWatch logs)
resource "aws_iam_role_policy_attachment" "ecs_execution_control_plane" {
  role       = aws_iam_role.ecs_execution_control_plane.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy_attachment" "ecs_execution_provider_worker" {
  role       = aws_iam_role.ecs_execution_provider_worker.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# Execution role: Control Plane gets API keys (Secrets Manager or SSM for dev)
resource "aws_iam_role_policy" "ecs_execution_control_plane_secrets" {
  name = "secrets"
  role = aws_iam_role.ecs_execution_control_plane.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = "secretsmanager:GetSecretValue"
        Resource = local.control_plane_secrets_arns
      },
      {
        Effect   = "Allow"
        Action   = "ssm:GetParameter"
        Resource = [local.ssm_api_keys_param_arn]
      }
    ]
  })
}

# Execution role: Provider Worker gets OpenAI key (Secrets Manager or SSM for dev)
resource "aws_iam_role_policy" "ecs_execution_provider_worker_secrets" {
  name = "secrets"
  role = aws_iam_role.ecs_execution_provider_worker.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = "secretsmanager:GetSecretValue"
        Resource = local.provider_worker_secrets_arns
      },
      {
        Effect   = "Allow"
        Action   = "ssm:GetParameter"
        Resource = [local.ssm_openai_key_param_arn]
      }
    ]
  })
}

# T-2.5.3: IAM role for Control Plane API task
resource "aws_iam_role" "control_plane" {
  name = "prompt-gateway-${var.environment}-control-plane-api"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
      }
    ]
  })

  tags = {
    Name        = "prompt-gateway-${var.environment}-control-plane-api"
    Environment = var.environment
  }
}

# T-2.5.4: DynamoDB policy for Control Plane
resource "aws_iam_role_policy" "control_plane_dynamodb" {
  name = "dynamodb"
  role = aws_iam_role.control_plane.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "dynamodb:GetItem",
          "dynamodb:PutItem",
          "dynamodb:UpdateItem",
          "dynamodb:DeleteItem",
          "dynamodb:Query",
          "dynamodb:BatchWriteItem",
          "dynamodb:TransactWriteItems"
        ]
        Resource = [
          var.dynamodb_table_arn,
          "${var.dynamodb_table_arn}/index/*"
        ]
      }
    ]
  })
}

# T-2.5.5: SQS policy for Control Plane
resource "aws_iam_role_policy" "control_plane_sqs" {
  name = "sqs"
  role = aws_iam_role.control_plane.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "sqs:SendMessage",
          "sqs:ReceiveMessage",
          "sqs:DeleteMessage",
          "sqs:GetQueueAttributes"
        ]
        Resource = [
          var.dispatch_queue_arn,
          var.result_queue_arn
        ]
      }
    ]
  })
}

# T-2.5.6: S3 policy for Control Plane
resource "aws_iam_role_policy" "control_plane_s3" {
  name = "s3"
  role = aws_iam_role.control_plane.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "s3:GetObject",
          "s3:PutObject"
        ]
        Resource = [
          "${var.prompts_bucket_arn}/*",
          "${var.results_bucket_arn}/*"
        ]
      }
    ]
  })
}

# T-2.5.7: Secrets Manager policy for Control Plane (API keys)
resource "aws_iam_role_policy" "control_plane_secrets" {
  name = "secrets"
  role = aws_iam_role.control_plane.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = "secretsmanager:GetSecretValue"
        Resource = local.control_plane_secrets_arns
      }
    ]
  })
}

# T-2.5.8: SSM policy for Control Plane (config)
resource "aws_iam_role_policy" "control_plane_ssm" {
  name = "ssm"
  role = aws_iam_role.control_plane.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action   = "ssm:GetParameter"
        Resource = [local.ssm_prefix]
      }
    ]
  })
}

# T-2.5.9: IAM role for Provider Worker task
resource "aws_iam_role" "provider_worker" {
  name = "prompt-gateway-${var.environment}-provider-worker"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
      }
    ]
  })

  tags = {
    Name        = "prompt-gateway-${var.environment}-provider-worker"
    Environment = var.environment
  }
}

# T-2.5.9: SQS policy for Provider Worker
resource "aws_iam_role_policy" "provider_worker_sqs" {
  name = "sqs"
  role = aws_iam_role.provider_worker.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "sqs:ReceiveMessage",
          "sqs:DeleteMessage",
          "sqs:SendMessage",
          "sqs:GetQueueAttributes"
        ]
        Resource = [
          var.dispatch_queue_arn,
          var.result_queue_arn
        ]
      }
    ]
  })
}

# T-2.5.9: DynamoDB policy for Provider Worker deduplication table
resource "aws_iam_role_policy" "provider_worker_dynamodb" {
  name = "dynamodb"
  role = aws_iam_role.provider_worker.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "dynamodb:GetItem",
          "dynamodb:PutItem",
          "dynamodb:UpdateItem"
        ]
        Resource = [var.dedupe_table_arn]
      }
    ]
  })
}

# T-2.5.9: S3 policy for Provider Worker
resource "aws_iam_role_policy" "provider_worker_s3" {
  name = "s3"
  role = aws_iam_role.provider_worker.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "s3:GetObject",
          "s3:PutObject"
        ]
        Resource = [
          "${var.prompts_bucket_arn}/*",
          "${var.results_bucket_arn}/*"
        ]
      }
    ]
  })
}

# T-2.5.9: Secrets Manager policy for Provider Worker (OpenAI key)
resource "aws_iam_role_policy" "provider_worker_secrets" {
  name = "secrets"
  role = aws_iam_role.provider_worker.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = "secretsmanager:GetSecretValue"
        Resource = local.provider_worker_secrets_arns
      }
    ]
  })
}
