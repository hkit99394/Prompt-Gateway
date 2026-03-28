# Monitoring module variables
# T-8.2 – T-8.6: CloudWatch alarms and SNS

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "alb_arn_suffix" {
  description = "ALB ARN suffix for CloudWatch dimension (e.g. app/name/id)"
  type        = string
}

variable "target_group_arn_suffix" {
  description = "Target group ARN suffix for CloudWatch dimension (e.g. targetgroup/name/id)"
  type        = string
}

variable "ecs_cluster_name" {
  description = "ECS cluster name"
  type        = string
}

variable "ecs_api_service_name" {
  description = "Control Plane API ECS service name"
  type        = string
}

variable "sqs_dlq_name" {
  description = "SQS dead-letter queue name (for ApproximateNumberOfMessagesVisible alarm)"
  type        = string
}

variable "dispatch_queue_name" {
  description = "Primary dispatch queue name for backlog alarms"
  type        = string
}

variable "result_queue_name" {
  description = "Primary result queue name for backlog alarms"
  type        = string
}

variable "dynamodb_table_name" {
  description = "DynamoDB main table name (for throttle alarms)"
  type        = string
}

variable "alarm_email" {
  description = "Optional email address for SNS alarm notifications. If empty, topic is created but no subscription is added."
  type        = string
  default     = ""
}

variable "api_5xx_threshold" {
  description = "Alarm when 5xx count exceeds this value in the evaluation period"
  type        = number
  default     = 5
}

variable "api_5xx_period_seconds" {
  description = "Period in seconds for API 5xx metric"
  type        = number
  default     = 300
}

variable "api_5xx_evaluation_periods" {
  description = "Number of periods the 5xx threshold must be breached"
  type        = number
  default     = 1
}

variable "ecs_cpu_threshold_percent" {
  description = "Alarm when ECS API service CPU utilization exceeds this percentage"
  type        = number
  default     = 85
}

variable "ecs_memory_threshold_percent" {
  description = "Alarm when ECS API service memory utilization exceeds this percentage"
  type        = number
  default     = 85
}

variable "dispatch_queue_visible_threshold" {
  description = "Alarm when visible messages on the dispatch queue exceed this threshold"
  type        = number
  default     = 10
}

variable "dispatch_queue_age_threshold_seconds" {
  description = "Alarm when the oldest message on the dispatch queue exceeds this age"
  type        = number
  default     = 300
}

variable "result_queue_visible_threshold" {
  description = "Alarm when visible messages on the result queue exceed this threshold"
  type        = number
  default     = 10
}

variable "result_queue_age_threshold_seconds" {
  description = "Alarm when the oldest message on the result queue exceeds this age"
  type        = number
  default     = 300
}

variable "provider_lambda_function_name" {
  description = "Provider worker Lambda function name for runtime alarms"
  type        = string
  default     = null
}

variable "result_lambda_function_name" {
  description = "Result ingestion Lambda function name for runtime alarms"
  type        = string
  default     = null
}

variable "outbox_lambda_function_name" {
  description = "Outbox dispatch Lambda function name for runtime alarms"
  type        = string
  default     = null
}

variable "provider_lambda_timeout_seconds" {
  description = "Provider worker Lambda timeout in seconds for duration alarms"
  type        = number
  default     = 120
}

variable "result_lambda_timeout_seconds" {
  description = "Result ingestion Lambda timeout in seconds for duration alarms"
  type        = number
  default     = 60
}

variable "outbox_lambda_timeout_seconds" {
  description = "Outbox dispatch Lambda timeout in seconds for duration alarms"
  type        = number
  default     = 60
}

variable "provider_lambda_reserved_concurrency" {
  description = "Provider worker Lambda reserved concurrency for pressure alarms"
  type        = number
  default     = 5
}

variable "result_lambda_reserved_concurrency" {
  description = "Result ingestion Lambda reserved concurrency for pressure alarms"
  type        = number
  default     = 5
}

variable "outbox_lambda_reserved_concurrency" {
  description = "Outbox dispatch Lambda reserved concurrency for pressure alarms"
  type        = number
  default     = 1
}
