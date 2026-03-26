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

variable "dispatch_queue_arn" {
  description = "Dispatch queue ARN"
  type        = string
}

variable "dispatch_queue_url" {
  description = "Dispatch queue URL"
  type        = string
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

variable "provider_lambda_timeout" {
  description = "Timeout in seconds for the provider Lambda"
  type        = number
  default     = 120
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

variable "provider_lambda_batch_size" {
  description = "SQS batch size for the provider Lambda"
  type        = number
  default     = 5
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
