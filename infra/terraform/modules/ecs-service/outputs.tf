# ECS service module outputs
# Implementation: T-2.6.10

output "cluster_name" {
  description = "ECS cluster name"
  value       = aws_ecs_cluster.main.name
}

output "api_service_name" {
  description = "Control Plane API service name"
  value       = aws_ecs_service.api.name
}

output "worker_service_name" {
  description = "Provider Worker service name"
  value       = aws_ecs_service.worker.name
}

output "alb_dns_name" {
  description = "ALB DNS name"
  value       = aws_lb.main.dns_name
}

output "alb_arn_suffix" {
  description = "ALB ARN suffix for CloudWatch dimensions (e.g. app/name/id)"
  value       = aws_lb.main.arn_suffix
}

output "api_target_group_arn_suffix" {
  description = "API target group ARN suffix for CloudWatch dimensions (e.g. targetgroup/name/id)"
  value       = aws_lb_target_group.api.arn_suffix
}

output "ecr_api_repo_url" {
  description = "ECR repository URL for Control Plane API"
  value       = aws_ecr_repository.control_plane_api.repository_url
}

output "ecr_worker_repo_url" {
  description = "ECR repository URL for Provider Worker"
  value       = aws_ecr_repository.provider_worker.repository_url
}
