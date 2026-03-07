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
  description = "GSI name for job listing"
  type        = string
  default     = "JobListIndex"
}
