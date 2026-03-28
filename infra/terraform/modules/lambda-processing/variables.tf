# Lambda processing module variables

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "enable" {
  description = "Whether to provision Lambda queue processors and the outbox scheduler"
  type        = bool
  default     = false
}

variable "enable_http_api" {
  description = "Whether to provision the Control Plane HTTP Lambda and API Gateway edge"
  type        = bool
  default     = false
}

variable "lambda_runtime" {
  description = "Managed Lambda runtime identifier for the .NET functions"
  type        = string
  default     = "dotnet8"
}

variable "provider_lambda_role_arn" {
  description = "Execution role ARN for the provider worker Lambda"
  type        = string
}

variable "result_lambda_role_arn" {
  description = "Execution role ARN for the result ingestion Lambda"
  type        = string
}

variable "outbox_lambda_role_arn" {
  description = "Execution role ARN for the outbox dispatch Lambda"
  type        = string
}

variable "control_plane_http_lambda_role_arn" {
  description = "Execution role ARN for the Control Plane HTTP Lambda"
  type        = string
}

variable "control_plane_http_authorizer_lambda_role_arn" {
  description = "Execution role ARN for the Control Plane HTTP authorizer Lambda"
  type        = string
}

variable "dispatch_queue_arn" {
  description = "Dispatch queue ARN"
  type        = string
}

variable "dispatch_queue_url" {
  description = "Dispatch queue URL"
  type        = string
}

variable "dispatch_queue_visibility_timeout_seconds" {
  description = "Dispatch queue visibility timeout in seconds"
  type        = number
}

variable "result_queue_arn" {
  description = "Result queue ARN"
  type        = string
}

variable "result_queue_url" {
  description = "Result queue URL"
  type        = string
}

variable "dynamodb_table_name" {
  description = "Primary DynamoDB table name"
  type        = string
}

variable "worker_dedupe_table_name" {
  description = "Provider worker dedupe table name"
  type        = string
}

variable "dynamodb_gsi_name" {
  description = "Primary DynamoDB GSI name"
  type        = string
}

variable "prompts_bucket_name" {
  description = "Prompt bucket name"
  type        = string
  default     = ""
}

variable "results_bucket_name" {
  description = "Results bucket name"
  type        = string
  default     = ""
}

variable "provider_lambda_package_path" {
  description = "Path to the provider Lambda deployment zip"
  type        = string
  default     = "artifacts/provider-worker-lambda.zip"
}

variable "result_lambda_package_path" {
  description = "Path to the result Lambda deployment zip"
  type        = string
  default     = "artifacts/control-plane-result-lambda.zip"
}

variable "outbox_lambda_package_path" {
  description = "Path to the outbox Lambda deployment zip"
  type        = string
  default     = "artifacts/control-plane-outbox-lambda.zip"
}

variable "control_plane_http_lambda_package_path" {
  description = "Path to the Control Plane HTTP Lambda deployment zip"
  type        = string
  default     = "artifacts/control-plane-api-lambda.zip"
}

variable "control_plane_http_authorizer_lambda_package_path" {
  description = "Path to the Control Plane HTTP authorizer Lambda deployment zip"
  type        = string
  default     = "artifacts/control-plane-authorizer-lambda.zip"
}

variable "provider_lambda_handler" {
  description = "Handler string for the provider Lambda"
  type        = string
  default     = "Provider.Worker.Lambda::Provider.Worker.Lambda.ProviderDispatchFunction::FunctionHandler"
}

variable "result_lambda_handler" {
  description = "Handler string for the result Lambda"
  type        = string
  default     = "ControlPlane.ResultLambda::ControlPlane.ResultLambda.ResultQueueFunction::FunctionHandler"
}

variable "outbox_lambda_handler" {
  description = "Handler string for the outbox Lambda"
  type        = string
  default     = "ControlPlane.OutboxLambda::ControlPlane.OutboxLambda.OutboxDispatchFunction::FunctionHandler"
}

variable "control_plane_http_lambda_handler" {
  description = "Handler string for the Control Plane HTTP Lambda"
  type        = string
  default     = "ControlPlane.Api.Lambda"
}

variable "control_plane_http_authorizer_lambda_handler" {
  description = "Handler string for the Control Plane HTTP authorizer Lambda"
  type        = string
  default     = "ControlPlane.Api.Authorizer::ControlPlane.Api.Authorizer.ApiKeyAuthorizerFunction::FunctionHandler"
}

variable "provider_lambda_timeout" {
  description = "Timeout in seconds for the provider Lambda"
  type        = number
  default     = 300
}

variable "result_lambda_timeout" {
  description = "Timeout in seconds for the result Lambda"
  type        = number
  default     = 60
}

variable "outbox_lambda_timeout" {
  description = "Timeout in seconds for the outbox Lambda"
  type        = number
  default     = 60
}

variable "control_plane_http_lambda_timeout" {
  description = "Timeout in seconds for the Control Plane HTTP Lambda"
  type        = number
  default     = 30
}

variable "control_plane_http_authorizer_lambda_timeout" {
  description = "Timeout in seconds for the Control Plane HTTP authorizer Lambda"
  type        = number
  default     = 10
}

variable "provider_lambda_memory_size" {
  description = "Memory size in MB for the provider Lambda"
  type        = number
  default     = 1024
}

variable "result_lambda_memory_size" {
  description = "Memory size in MB for the result Lambda"
  type        = number
  default     = 512
}

variable "outbox_lambda_memory_size" {
  description = "Memory size in MB for the outbox Lambda"
  type        = number
  default     = 512
}

variable "control_plane_http_lambda_memory_size" {
  description = "Memory size in MB for the Control Plane HTTP Lambda"
  type        = number
  default     = 1024
}

variable "control_plane_http_authorizer_lambda_memory_size" {
  description = "Memory size in MB for the Control Plane HTTP authorizer Lambda"
  type        = number
  default     = 256
}

variable "provider_lambda_reserved_concurrency" {
  description = "Reserved concurrency for the provider Lambda"
  type        = number
  default     = 5
}

variable "result_lambda_reserved_concurrency" {
  description = "Reserved concurrency for the result Lambda"
  type        = number
  default     = 5
}

variable "outbox_lambda_reserved_concurrency" {
  description = "Reserved concurrency for the outbox Lambda"
  type        = number
  default     = 1
}

variable "control_plane_http_lambda_reserved_concurrency" {
  description = "Reserved concurrency for the Control Plane HTTP Lambda"
  type        = number
  default     = 5
}

variable "control_plane_http_authorizer_lambda_reserved_concurrency" {
  description = "Reserved concurrency for the Control Plane HTTP authorizer Lambda"
  type        = number
  default     = 5
}

variable "provider_lambda_batch_size" {
  description = "SQS batch size for the provider Lambda"
  type        = number
  default     = 1
}

variable "provider_worker_openai_timeout_seconds" {
  description = "OpenAI request timeout in seconds used by the provider runtime"
  type        = number
  default     = 90
}

variable "provider_worker_openai_retry_max_attempts" {
  description = "Maximum number of OpenAI attempts per provider message"
  type        = number
  default     = 3
}

variable "provider_worker_openai_retry_max_backoff_seconds" {
  description = "Maximum backoff cap in seconds between OpenAI retry attempts"
  type        = number
  default     = 10
}

variable "provider_worker_processing_overhead_buffer_seconds" {
  description = "Additional safety buffer in seconds for prompt loading, payload storage, and result publication"
  type        = number
  default     = 15
}

variable "result_lambda_batch_size" {
  description = "SQS batch size for the result Lambda"
  type        = number
  default     = 10
}

variable "outbox_schedule_expression" {
  description = "EventBridge schedule expression for the outbox Lambda"
  type        = string
  default     = "rate(1 minute)"
}

variable "outbox_max_messages_per_invocation" {
  description = "Maximum number of outbox messages to dispatch per scheduled invocation"
  type        = number
  default     = 25
}

variable "log_retention_in_days" {
  description = "CloudWatch log retention for Lambda log groups"
  type        = number
  default     = 14
}
