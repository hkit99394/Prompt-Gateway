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

output "control_plane_http_lambda_function_name" {
  description = "Control Plane HTTP Lambda function name"
  value       = var.enable_http_api ? aws_lambda_function.control_plane_http[0].function_name : null
}

output "control_plane_http_api_invoke_url" {
  description = "Invoke URL for the Control Plane HTTP API Gateway"
  value       = var.enable_http_api ? aws_apigatewayv2_stage.control_plane_http_default[0].invoke_url : null
}

output "provider_lambda_timeout_seconds" {
  description = "Provider worker Lambda timeout in seconds"
  value       = var.provider_lambda_timeout
}

output "result_lambda_timeout_seconds" {
  description = "Result ingestion Lambda timeout in seconds"
  value       = var.result_lambda_timeout
}

output "outbox_lambda_timeout_seconds" {
  description = "Outbox dispatch Lambda timeout in seconds"
  value       = var.outbox_lambda_timeout
}

output "control_plane_http_lambda_timeout_seconds" {
  description = "Control Plane HTTP Lambda timeout in seconds"
  value       = var.control_plane_http_lambda_timeout
}

output "provider_lambda_reserved_concurrency" {
  description = "Provider worker Lambda reserved concurrency"
  value       = var.provider_lambda_reserved_concurrency
}

output "result_lambda_reserved_concurrency" {
  description = "Result ingestion Lambda reserved concurrency"
  value       = var.result_lambda_reserved_concurrency
}

output "outbox_lambda_reserved_concurrency" {
  description = "Outbox dispatch Lambda reserved concurrency"
  value       = var.outbox_lambda_reserved_concurrency
}

output "control_plane_http_lambda_reserved_concurrency" {
  description = "Control Plane HTTP Lambda reserved concurrency"
  value       = var.control_plane_http_lambda_reserved_concurrency
}
