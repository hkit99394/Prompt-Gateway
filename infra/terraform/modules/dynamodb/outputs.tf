# DynamoDB module outputs
# Implementation: T-2.3.6

output "table_name" {
  description = "DynamoDB table name"
  value       = aws_dynamodb_table.main.name
}

output "table_arn" {
  description = "DynamoDB table ARN"
  value       = aws_dynamodb_table.main.arn
}

output "dedupe_table_name" {
  description = "DynamoDB deduplication table name for Provider Worker"
  value       = aws_dynamodb_table.worker_dedupe.name
}

output "dedupe_table_arn" {
  description = "DynamoDB deduplication table ARN for Provider Worker"
  value       = aws_dynamodb_table.worker_dedupe.arn
}

output "gsi_name" {
  description = "GSI name for job listing"
  value       = var.gsi_name
}
