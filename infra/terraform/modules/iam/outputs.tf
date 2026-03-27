# IAM module outputs
# Implementation: T-2.5.10

output "ecs_execution_control_plane_role_arn" {
  description = "ECS task execution role ARN for Control Plane API (ECR, logs, API keys secret)"
  value       = aws_iam_role.ecs_execution_control_plane.arn
}

output "ecs_execution_provider_worker_role_arn" {
  description = "ECS task execution role ARN for Provider Worker (ECR, logs, OpenAI key secret)"
  value       = aws_iam_role.ecs_execution_provider_worker.arn
}

output "control_plane_task_role_arn" {
  description = "Control Plane API task role ARN"
  value       = aws_iam_role.control_plane.arn
}

output "provider_worker_task_role_arn" {
  description = "Provider Worker task role ARN"
  value       = aws_iam_role.provider_worker.arn
}

output "provider_worker_lambda_role_arn" {
  description = "Provider worker Lambda role ARN"
  value       = aws_iam_role.provider_worker_lambda.arn
}

output "result_lambda_role_arn" {
  description = "Result ingestion Lambda role ARN"
  value       = aws_iam_role.result_lambda.arn
}

output "outbox_lambda_role_arn" {
  description = "Outbox dispatch Lambda role ARN"
  value       = aws_iam_role.outbox_lambda.arn
}

output "control_plane_http_lambda_role_arn" {
  description = "Control plane HTTP Lambda role ARN"
  value       = aws_iam_role.control_plane_http_lambda.arn
}
