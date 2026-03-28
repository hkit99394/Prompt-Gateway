# IAM module variables
# Implementation: T-2.5.x

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "dynamodb_table_arn" {
  description = "DynamoDB table ARN for policy"
  type        = string
}

variable "dedupe_table_arn" {
  description = "DynamoDB dedupe table ARN for Provider Worker policy"
  type        = string
}

variable "dispatch_queue_arn" {
  description = "Dispatch queue ARN for policy"
  type        = string
}

variable "result_queue_arn" {
  description = "Result queue ARN for policy"
  type        = string
}

variable "prompts_bucket_arn" {
  description = "S3 prompts bucket ARN (optional until S3 module implemented)"
  type        = string
  default     = ""
}

variable "results_bucket_arn" {
  description = "S3 results bucket ARN (optional until S3 module implemented)"
  type        = string
  default     = ""
}
