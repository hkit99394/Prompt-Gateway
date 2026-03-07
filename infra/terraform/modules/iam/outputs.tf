# IAM module outputs
# Implementation: T-2.5.10

output "ecs_execution_role_arn" {
  description = "ECS task execution role ARN"
  value       = aws_iam_role.ecs_execution.arn
}

output "control_plane_task_role_arn" {
  description = "Control Plane API task role ARN"
  value       = aws_iam_role.control_plane.arn
}

output "provider_worker_task_role_arn" {
  description = "Provider Worker task role ARN"
  value       = aws_iam_role.provider_worker.arn
}
