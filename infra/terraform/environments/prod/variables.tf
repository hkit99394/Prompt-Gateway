# Prod environment variables

variable "aws_region" {
  description = "AWS region"
  type        = string
  default     = "us-east-1"
}

variable "environment" {
  description = "Environment name"
  type        = string
  default     = "prod"
}

variable "certificate_arn" {
  description = "ACM certificate ARN for HTTPS (required for prod)"
  type        = string
}

variable "alarm_email" {
  description = "Optional email for CloudWatch alarm SNS notifications (T-8.6)"
  type        = string
  default     = ""
}
