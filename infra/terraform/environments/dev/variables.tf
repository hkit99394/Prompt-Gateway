# Dev environment variables

variable "aws_region" {
  description = "AWS region"
  type        = string
  default     = "us-east-1"
}

variable "environment" {
  description = "Environment name"
  type        = string
  default     = "dev"
}

variable "alarm_email" {
  description = "Optional email for CloudWatch alarm SNS notifications (T-8.6). If empty, alarms still fire but no subscription is created."
  type        = string
  default     = ""
}
