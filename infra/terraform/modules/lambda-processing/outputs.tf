output "provider_lambda_function_name" {
  description = "Provider worker Lambda function name"
  value       = var.enable ? aws_lambda_function.provider[0].function_name : null
}

output "result_lambda_function_name" {
  description = "Result ingestion Lambda function name"
  value       = var.enable ? aws_lambda_function.result[0].function_name : null
}

output "outbox_lambda_function_name" {
  description = "Outbox dispatch Lambda function name"
  value       = var.enable ? aws_lambda_function.outbox[0].function_name : null
}
