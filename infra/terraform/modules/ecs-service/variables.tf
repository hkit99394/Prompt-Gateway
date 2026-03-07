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
