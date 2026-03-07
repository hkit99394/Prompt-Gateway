# S3 module variables
# Implementation: T-2.8.x

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "bucket_name_prefix" {
  description = "Prefix for bucket names"
  type        = string
  default     = "prompt-gateway"
}
