# IAM module outputs
# Implementation: T-2.5.x

output "ecs_execution_role_arn" {
  description = "ECS task execution role ARN"
  value       = null
}

output "control_plane_task_role_arn" {
  description = "Control Plane API task role ARN"
  value       = null
}

output "provider_worker_task_role_arn" {
  description = "Provider Worker task role ARN"
  value       = null
}
