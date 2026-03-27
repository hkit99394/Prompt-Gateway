data "aws_caller_identity" "current" {}
data "aws_region" "current" {}

locals {
  processing_enabled    = var.enable
  http_api_enabled      = var.enable_http_api
  account_id            = data.aws_caller_identity.current.account_id
  region                = data.aws_region.current.name
  openai_key_secret_arn = "arn:aws:secretsmanager:${local.region}:${local.account_id}:secret:prompt-gateway/${var.environment}/openai-api-key"
  api_keys_secret_arn   = "arn:aws:secretsmanager:${local.region}:${local.account_id}:secret:prompt-gateway/${var.environment}/api-keys"
  openai_key_value_from = lower(var.environment) == "dev" ? "arn:aws:ssm:${local.region}:${local.account_id}:parameter/prompt-gateway/${var.environment}/openai-api-key" : local.openai_key_secret_arn
  api_keys_value_from   = lower(var.environment) == "dev" ? "arn:aws:ssm:${local.region}:${local.account_id}:parameter/prompt-gateway/${var.environment}/api-keys" : local.api_keys_secret_arn
}

resource "aws_cloudwatch_log_group" "provider" {
  count = local.processing_enabled ? 1 : 0

  name              = "/aws/lambda/prompt-gateway-${var.environment}-provider-worker"
  retention_in_days = var.log_retention_in_days

  tags = {
    Name        = "prompt-gateway-${var.environment}-provider-worker"
    Environment = var.environment
  }
}

resource "aws_cloudwatch_log_group" "result" {
  count = local.processing_enabled ? 1 : 0

  name              = "/aws/lambda/prompt-gateway-${var.environment}-result-ingestion"
  retention_in_days = var.log_retention_in_days

  tags = {
    Name        = "prompt-gateway-${var.environment}-result-ingestion"
    Environment = var.environment
  }
}

resource "aws_cloudwatch_log_group" "outbox" {
  count = local.processing_enabled ? 1 : 0

  name              = "/aws/lambda/prompt-gateway-${var.environment}-outbox-dispatch"
  retention_in_days = var.log_retention_in_days

  tags = {
    Name        = "prompt-gateway-${var.environment}-outbox-dispatch"
    Environment = var.environment
  }
}

resource "aws_cloudwatch_log_group" "control_plane_http" {
  count = local.http_api_enabled ? 1 : 0

  name              = "/aws/lambda/prompt-gateway-${var.environment}-control-plane-http"
  retention_in_days = var.log_retention_in_days

  tags = {
    Name        = "prompt-gateway-${var.environment}-control-plane-http"
    Environment = var.environment
  }
}

resource "aws_lambda_function" "provider" {
  count = local.processing_enabled ? 1 : 0

  function_name                  = "prompt-gateway-${var.environment}-provider-worker"
  role                           = var.provider_lambda_role_arn
  runtime                        = var.lambda_runtime
  handler                        = var.provider_lambda_handler
  filename                       = var.provider_lambda_package_path
  source_code_hash               = filebase64sha256(var.provider_lambda_package_path)
  timeout                        = var.provider_lambda_timeout
  memory_size                    = var.provider_lambda_memory_size
  publish                        = true
  reserved_concurrent_executions = var.provider_lambda_reserved_concurrency

  environment {
    variables = merge(
      {
        ProviderWorker__InputQueueUrl           = var.dispatch_queue_url
        ProviderWorker__OutputQueueUrl          = var.result_queue_url
        ProviderWorker__DedupeTableName         = var.worker_dedupe_table_name
        ProviderWorker__OpenAi__ApiKeyValueFrom = local.openai_key_value_from
      },
      var.prompts_bucket_name != "" ? { ProviderWorker__PromptBucket = var.prompts_bucket_name } : {},
      var.results_bucket_name != "" ? { ProviderWorker__ResultBucket = var.results_bucket_name } : {}
    )
  }

  depends_on = [aws_cloudwatch_log_group.provider]
}

resource "aws_lambda_function" "result" {
  count = local.processing_enabled ? 1 : 0

  function_name                  = "prompt-gateway-${var.environment}-result-ingestion"
  role                           = var.result_lambda_role_arn
  runtime                        = var.lambda_runtime
  handler                        = var.result_lambda_handler
  filename                       = var.result_lambda_package_path
  source_code_hash               = filebase64sha256(var.result_lambda_package_path)
  timeout                        = var.result_lambda_timeout
  memory_size                    = var.result_lambda_memory_size
  publish                        = true
  reserved_concurrent_executions = var.result_lambda_reserved_concurrency

  environment {
    variables = {
      AwsQueue__DispatchQueueUrl   = var.dispatch_queue_url
      AwsQueue__ResultQueueUrl     = var.result_queue_url
      AwsStorage__TableName        = var.dynamodb_table_name
      AwsStorage__JobListIndexName = var.dynamodb_gsi_name
    }
  }

  depends_on = [aws_cloudwatch_log_group.result]
}

resource "aws_lambda_function" "outbox" {
  count = local.processing_enabled ? 1 : 0

  function_name                  = "prompt-gateway-${var.environment}-outbox-dispatch"
  role                           = var.outbox_lambda_role_arn
  runtime                        = var.lambda_runtime
  handler                        = var.outbox_lambda_handler
  filename                       = var.outbox_lambda_package_path
  source_code_hash               = filebase64sha256(var.outbox_lambda_package_path)
  timeout                        = var.outbox_lambda_timeout
  memory_size                    = var.outbox_lambda_memory_size
  publish                        = true
  reserved_concurrent_executions = var.outbox_lambda_reserved_concurrency

  environment {
    variables = {
      AwsQueue__DispatchQueueUrl             = var.dispatch_queue_url
      AwsStorage__TableName                  = var.dynamodb_table_name
      AwsStorage__JobListIndexName           = var.dynamodb_gsi_name
      OutboxLambda__MaxMessagesPerInvocation = tostring(var.outbox_max_messages_per_invocation)
    }
  }

  depends_on = [aws_cloudwatch_log_group.outbox]
}

resource "aws_lambda_function" "control_plane_http" {
  count = local.http_api_enabled ? 1 : 0

  function_name                  = "prompt-gateway-${var.environment}-control-plane-http"
  role                           = var.control_plane_http_lambda_role_arn
  runtime                        = var.lambda_runtime
  handler                        = var.control_plane_http_lambda_handler
  filename                       = var.control_plane_http_lambda_package_path
  source_code_hash               = filebase64sha256(var.control_plane_http_lambda_package_path)
  timeout                        = var.control_plane_http_lambda_timeout
  memory_size                    = var.control_plane_http_lambda_memory_size
  publish                        = true
  reserved_concurrent_executions = var.control_plane_http_lambda_reserved_concurrency

  environment {
    variables = {
      ApiSecurity__ApiKeyValueFrom                = local.api_keys_value_from
      AwsQueue__DispatchQueueUrl                  = var.dispatch_queue_url
      AwsQueue__ResultQueueUrl                    = var.result_queue_url
      AwsStorage__TableName                       = var.dynamodb_table_name
      AwsStorage__JobListIndexName                = var.dynamodb_gsi_name
      ControlPlaneApi__EnableSwagger              = "false"
      HostedWorkers__EnablePostAcceptResumeWorker = "false"
      HostedWorkers__EnableOutboxWorker           = "false"
      HostedWorkers__EnableResultQueueWorker      = "false"
    }
  }

  depends_on = [aws_cloudwatch_log_group.control_plane_http]
}

resource "aws_apigatewayv2_api" "control_plane_http" {
  count = local.http_api_enabled ? 1 : 0

  name          = "prompt-gateway-${var.environment}-control-plane-http"
  protocol_type = "HTTP"

  cors_configuration {
    allow_headers = ["content-type", "x-api-key", "x-idempotency-key"]
    allow_methods = ["GET", "POST", "OPTIONS"]
    allow_origins = ["*"]
    max_age       = 300
  }
}

resource "aws_apigatewayv2_integration" "control_plane_http" {
  count = local.http_api_enabled ? 1 : 0

  api_id                 = aws_apigatewayv2_api.control_plane_http[0].id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.control_plane_http[0].invoke_arn
  integration_method     = "POST"
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "control_plane_http_default" {
  count = local.http_api_enabled ? 1 : 0

  api_id    = aws_apigatewayv2_api.control_plane_http[0].id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.control_plane_http[0].id}"
}

resource "aws_apigatewayv2_stage" "control_plane_http_default" {
  count = local.http_api_enabled ? 1 : 0

  api_id      = aws_apigatewayv2_api.control_plane_http[0].id
  name        = "$default"
  auto_deploy = true
}

resource "aws_lambda_permission" "allow_control_plane_http_api" {
  count = local.http_api_enabled ? 1 : 0

  statement_id  = "AllowExecutionFromApiGateway"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.control_plane_http[0].function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.control_plane_http[0].execution_arn}/*/*"
}

resource "aws_lambda_event_source_mapping" "provider" {
  count = local.processing_enabled ? 1 : 0

  event_source_arn        = var.dispatch_queue_arn
  function_name           = aws_lambda_function.provider[0].arn
  batch_size              = var.provider_lambda_batch_size
  function_response_types = ["ReportBatchItemFailures"]
}

resource "aws_lambda_event_source_mapping" "result" {
  count = local.processing_enabled ? 1 : 0

  event_source_arn        = var.result_queue_arn
  function_name           = aws_lambda_function.result[0].arn
  batch_size              = var.result_lambda_batch_size
  function_response_types = ["ReportBatchItemFailures"]
}

resource "aws_cloudwatch_event_rule" "outbox_schedule" {
  count = local.processing_enabled ? 1 : 0

  name                = "prompt-gateway-${var.environment}-outbox-dispatch"
  description         = "Scheduled outbox dispatch for Prompt Gateway"
  schedule_expression = var.outbox_schedule_expression
}

resource "aws_cloudwatch_event_target" "outbox_lambda" {
  count = local.processing_enabled ? 1 : 0

  rule = aws_cloudwatch_event_rule.outbox_schedule[0].name
  arn  = aws_lambda_function.outbox[0].arn
}

resource "aws_lambda_permission" "allow_outbox_schedule" {
  count = local.processing_enabled ? 1 : 0

  statement_id  = "AllowExecutionFromEventBridge"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.outbox[0].function_name
  principal     = "events.amazonaws.com"
  source_arn    = aws_cloudwatch_event_rule.outbox_schedule[0].arn
}
