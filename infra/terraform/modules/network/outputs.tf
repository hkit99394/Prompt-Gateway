# Network module outputs
# Implementation: T-2.2.10

output "vpc_id" {
  description = "VPC ID"
  value       = aws_vpc.main.id
}

output "private_subnet_ids" {
  description = "Private subnet IDs for ECS"
  value       = aws_subnet.private[*].id
}

output "public_subnet_ids" {
  description = "Public subnet IDs for ALB"
  value       = aws_subnet.public[*].id
}

output "alb_security_group_id" {
  description = "Security group ID for ALB"
  value       = aws_security_group.alb.id
}

output "ecs_api_security_group_id" {
  description = "Security group ID for ECS API tasks"
  value       = aws_security_group.ecs_api.id
}

output "ecs_worker_security_group_id" {
  description = "Security group ID for ECS worker tasks"
  value       = aws_security_group.ecs_worker.id
}
