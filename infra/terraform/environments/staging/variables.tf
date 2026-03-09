# Staging environment variables

variable "aws_region" {
  description = "AWS region"
  type        = string
  default     = "us-east-1"
}

variable "environment" {
  description = "Environment name"
  type        = string
  default     = "staging"
}

variable "certificate_arn" {
  description = "ACM certificate ARN for HTTPS (required for staging)"
  type        = string
}

variable "alarm_email" {
  description = "Optional email for CloudWatch alarm SNS notifications (T-8.6)"
  type        = string
  default     = ""
}
