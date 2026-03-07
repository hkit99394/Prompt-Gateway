# ECS service module outputs
# Implementation: T-2.6.x

output "cluster_name" {
  description = "ECS cluster name"
  value       = null
}

output "api_service_name" {
  description = "Control Plane API service name"
  value       = null
}

output "worker_service_name" {
  description = "Provider Worker service name"
  value       = null
}

output "alb_dns_name" {
  description = "ALB DNS name"
  value       = null
}

output "ecr_api_repo_url" {
  description = "ECR repository URL for Control Plane API"
  value       = null
}

output "ecr_worker_repo_url" {
  description = "ECR repository URL for Provider Worker"
  value       = null
}
