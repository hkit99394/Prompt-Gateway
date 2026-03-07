# S3 module outputs
# Implementation: T-2.7.3

output "prompts_bucket_name" {
  description = "S3 bucket name for prompts"
  value       = aws_s3_bucket.prompts.id
}

output "prompts_bucket_arn" {
  description = "S3 bucket ARN for prompts"
  value       = aws_s3_bucket.prompts.arn
}

output "results_bucket_name" {
  description = "S3 bucket name for results"
  value       = aws_s3_bucket.results.id
}

output "results_bucket_arn" {
  description = "S3 bucket ARN for results"
  value       = aws_s3_bucket.results.arn
}
