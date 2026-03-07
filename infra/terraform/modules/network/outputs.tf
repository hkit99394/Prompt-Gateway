# Network module outputs
# Implementation: T-2.2.x

output "vpc_id" {
  description = "VPC ID"
  value       = null
}

output "private_subnet_ids" {
  description = "Private subnet IDs for ECS"
  value       = null
}

output "alb_security_group_id" {
  description = "Security group ID for ALB"
  value       = null
}

output "ecs_api_security_group_id" {
  description = "Security group ID for ECS API tasks"
  value       = null
}

output "ecs_worker_security_group_id" {
  description = "Security group ID for ECS worker tasks"
  value       = null
}
