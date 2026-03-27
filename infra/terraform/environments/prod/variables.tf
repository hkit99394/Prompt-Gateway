# Prod environment variables

variable "aws_region" {
  description = "AWS region"
  type        = string
  default     = "us-east-1"
}

variable "environment" {
  description = "Environment name"
  type        = string
  default     = "prod"
}

variable "api_image_tag" {
  description = "Immutable image tag for the Control Plane API ECS task definition"
  type        = string
  default     = "bootstrap"
}

variable "worker_image_tag" {
  description = "Immutable image tag for the Provider Worker ECS task definition"
  type        = string
  default     = "bootstrap"
}

variable "certificate_arn" {
  description = "ACM certificate ARN for HTTPS (required for prod)"
  type        = string
}

variable "alarm_email" {
  description = "Optional email for CloudWatch alarm SNS notifications (T-8.6)"
  type        = string
  default     = ""
}

variable "enable_lambda_processing" {
  description = "Provision Lambda queue processors and the scheduled outbox dispatcher"
  type        = bool
  default     = false
}

variable "enable_lambda_http_api" {
  description = "Provision the Control Plane HTTP Lambda and API Gateway edge"
  type        = bool
  default     = false
}

variable "lambda_runtime" {
  description = "Managed Lambda runtime identifier for the .NET functions"
  type        = string
  default     = "dotnet8"
}

variable "provider_lambda_package_path" {
  description = "Path to the provider Lambda zip package"
  type        = string
  default     = "artifacts/provider-worker-lambda.zip"
}

variable "result_lambda_package_path" {
  description = "Path to the result Lambda zip package"
  type        = string
  default     = "artifacts/control-plane-result-lambda.zip"
}

variable "outbox_lambda_package_path" {
  description = "Path to the outbox Lambda zip package"
  type        = string
  default     = "artifacts/control-plane-outbox-lambda.zip"
}

variable "control_plane_http_lambda_package_path" {
  description = "Path to the Control Plane HTTP Lambda zip package"
  type        = string
  default     = "artifacts/control-plane-api-lambda.zip"
}
