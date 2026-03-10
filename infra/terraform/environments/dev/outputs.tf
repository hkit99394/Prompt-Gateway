# Dev environment outputs for deployment scripts and verification

output "aws_region" {
  description = "AWS region used by this environment"
  value       = var.aws_region
}

output "environment_name" {
  description = "Environment name used by this environment"
  value       = var.environment
}

output "dynamodb_table_name" {
  description = "Control Plane DynamoDB table name"
  value       = module.dynamodb.table_name
}

output "dynamodb_gsi_name" {
  description = "Control Plane DynamoDB job list GSI name"
  value       = module.dynamodb.gsi_name
}

output "dispatch_queue_url" {
  description = "Dispatch queue URL"
  value       = module.sqs.dispatch_queue_url
}

output "result_queue_url" {
  description = "Result queue URL"
  value       = module.sqs.result_queue_url
}

output "dlq_url" {
  description = "DLQ URL"
  value       = module.sqs.dlq_url
}

output "prompts_bucket_name" {
  description = "Prompts bucket name"
  value       = module.s3.prompts_bucket_name
}

output "results_bucket_name" {
  description = "Results bucket name"
  value       = module.s3.results_bucket_name
}

output "ecs_cluster_name" {
  description = "ECS cluster name"
  value       = module.ecs_service.cluster_name
}

output "ecr_api_repo_url" {
  description = "ECR API repository URL"
  value       = module.ecs_service.ecr_api_repo_url
}

output "ecr_worker_repo_url" {
  description = "ECR worker repository URL"
  value       = module.ecs_service.ecr_worker_repo_url
}
