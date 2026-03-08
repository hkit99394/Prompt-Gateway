# ECS service module variables
# Implementation: T-2.6.x

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "vpc_id" {
  description = "VPC ID"
  type        = string
}

variable "private_subnet_ids" {
  description = "Private subnet IDs for ECS tasks"
  type        = list(string)
}

variable "public_subnet_ids" {
  description = "Public subnet IDs for ALB"
  type        = list(string)
}

variable "alb_security_group_id" {
  description = "ALB security group ID"
  type        = string
}

variable "ecs_api_security_group_id" {
  description = "ECS API security group ID"
  type        = string
}

variable "ecs_worker_security_group_id" {
  description = "ECS worker security group ID"
  type        = string
}

variable "ecs_execution_control_plane_role_arn" {
  description = "ECS task execution role ARN for Control Plane API"
  type        = string
}

variable "ecs_execution_provider_worker_role_arn" {
  description = "ECS task execution role ARN for Provider Worker"
  type        = string
}

variable "control_plane_task_role_arn" {
  description = "Control Plane API task role ARN"
  type        = string
}

variable "provider_worker_task_role_arn" {
  description = "Provider Worker task role ARN"
  type        = string
}

variable "dynamodb_table_name" {
  description = "DynamoDB table name"
  type        = string
}

variable "worker_dedupe_table_name" {
  description = "DynamoDB table name for Provider Worker deduplication entries"
  type        = string
}

variable "dynamodb_gsi_name" {
  description = "DynamoDB GSI name for job listing"
  type        = string
}

variable "dispatch_queue_url" {
  description = "SQS dispatch queue URL"
  type        = string
}

variable "result_queue_url" {
  description = "SQS result queue URL"
  type        = string
}

variable "prompts_bucket_name" {
  description = "S3 prompts bucket name (optional; when set, passed to Provider Worker)"
  type        = string
  default     = ""
}

variable "results_bucket_name" {
  description = "S3 results bucket name (optional; when set, passed to Provider Worker)"
  type        = string
  default     = ""
}

variable "api_desired_count" {
  description = "Desired count for Control Plane API service"
  type        = number
  default     = 1
}

variable "worker_desired_count" {
  description = "Desired count for Provider Worker service"
  type        = number
  default     = 1
}

variable "api_cpu" {
  description = "CPU units for API task (1024 = 1 vCPU)"
  type        = number
  default     = 256
}

variable "api_memory" {
  description = "Memory (MiB) for API task"
  type        = number
  default     = 512
}

variable "worker_cpu" {
  description = "CPU units for worker task (1024 = 1 vCPU)"
  type        = number
  default     = 256
}

variable "worker_memory" {
  description = "Memory (MiB) for worker task"
  type        = number
  default     = 512
}

variable "certificate_arn" {
  description = "ACM certificate ARN for HTTPS listener (optional; when empty, uses HTTP on port 80)"
  type        = string
  default     = ""
}
