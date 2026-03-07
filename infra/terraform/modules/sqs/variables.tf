# SQS module variables
# Implementation: T-2.4.x

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "queue_name_prefix" {
  description = "Prefix for queue names"
  type        = string
  default     = "prompt-gateway"
}
