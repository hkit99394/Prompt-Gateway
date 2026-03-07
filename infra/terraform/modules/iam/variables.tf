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

variable "dispatch_queue_arn" {
  description = "Dispatch queue ARN for policy"
  type        = string
}

variable "result_queue_arn" {
  description = "Result queue ARN for policy"
  type        = string
}
