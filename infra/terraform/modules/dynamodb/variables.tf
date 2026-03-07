# DynamoDB module variables
# Implementation: T-2.3.x

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "table_name" {
  description = "DynamoDB table name"
  type        = string
}

variable "gsi_name" {
  description = "GSI name for job listing (must match DynamoDbOptions.JobListIndexName)"
  type        = string
  default     = "JobListIndex"
}

variable "billing_mode" {
  description = "Billing mode: PAY_PER_REQUEST (on-demand) or PROVISIONED"
  type        = string
  default     = "PAY_PER_REQUEST"
}
