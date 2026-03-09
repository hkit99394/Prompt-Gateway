# SQS module outputs
# Implementation: T-2.4.6

output "dispatch_queue_url" {
  description = "Dispatch queue URL"
  value       = aws_sqs_queue.dispatch.url
}

output "result_queue_url" {
  description = "Result queue URL"
  value       = aws_sqs_queue.result.url
}

output "dlq_url" {
  description = "Dead-letter queue URL"
  value       = aws_sqs_queue.dlq.url
}

output "dlq_name" {
  description = "Dead-letter queue name (for CloudWatch alarm dimension)"
  value       = aws_sqs_queue.dlq.name
}

output "dispatch_queue_arn" {
  description = "Dispatch queue ARN"
  value       = aws_sqs_queue.dispatch.arn
}

output "result_queue_arn" {
  description = "Result queue ARN"
  value       = aws_sqs_queue.result.arn
}
