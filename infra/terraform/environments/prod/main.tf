# Prod environment - root module
# Implementation: T-2.8.3

terraform {
  required_version = ">= 1.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project     = "Prompt Gateway"
      Environment = var.environment
    }
  }
}

module "network" {
  source = "../../modules/network"

  environment        = var.environment
  vpc_cidr           = "10.0.0.0/16"
  single_nat_gateway = false
}

module "dynamodb" {
  source = "../../modules/dynamodb"

  environment       = var.environment
  table_name        = "prompt-gateway-${var.environment}"
  dedupe_table_name = "prompt-gateway-${var.environment}-worker-dedupe"
  gsi_name          = "JobListIndex"
}

module "sqs" {
  source = "../../modules/sqs"

  environment = var.environment
}

module "s3" {
  source = "../../modules/s3"

  environment = var.environment
}

module "iam" {
  source = "../../modules/iam"

  environment        = var.environment
  dynamodb_table_arn = module.dynamodb.table_arn
  dedupe_table_arn   = module.dynamodb.dedupe_table_arn
  dispatch_queue_arn = module.sqs.dispatch_queue_arn
  result_queue_arn   = module.sqs.result_queue_arn
  prompts_bucket_arn = module.s3.prompts_bucket_arn
  results_bucket_arn = module.s3.results_bucket_arn
}

module "ecs_service" {
  source = "../../modules/ecs-service"

  environment                            = var.environment
  vpc_id                                 = module.network.vpc_id
  private_subnet_ids                     = module.network.private_subnet_ids
  public_subnet_ids                      = module.network.public_subnet_ids
  alb_security_group_id                  = module.network.alb_security_group_id
  ecs_api_security_group_id              = module.network.ecs_api_security_group_id
  ecs_worker_security_group_id           = module.network.ecs_worker_security_group_id
  ecs_execution_control_plane_role_arn   = module.iam.ecs_execution_control_plane_role_arn
  ecs_execution_provider_worker_role_arn = module.iam.ecs_execution_provider_worker_role_arn
  control_plane_task_role_arn            = module.iam.control_plane_task_role_arn
  provider_worker_task_role_arn          = module.iam.provider_worker_task_role_arn
  dynamodb_table_name                    = module.dynamodb.table_name
  worker_dedupe_table_name               = module.dynamodb.dedupe_table_name
  dynamodb_gsi_name                      = module.dynamodb.gsi_name
  dispatch_queue_url                     = module.sqs.dispatch_queue_url
  result_queue_url                       = module.sqs.result_queue_url
  prompts_bucket_name                    = module.s3.prompts_bucket_name
  results_bucket_name                    = module.s3.results_bucket_name
  api_desired_count                      = 2
  worker_desired_count                   = 2
  disable_api_hosted_workers             = var.enable_lambda_processing
  api_cpu                                = 1024
  api_memory                             = 2048
  worker_cpu                             = 1024
  worker_memory                          = 2048
  certificate_arn                        = var.certificate_arn
}

module "lambda_processing" {
  source = "../../modules/lambda-processing"

  environment                  = var.environment
  enable                       = var.enable_lambda_processing
  lambda_runtime               = var.lambda_runtime
  provider_lambda_role_arn     = module.iam.provider_worker_lambda_role_arn
  result_lambda_role_arn       = module.iam.result_lambda_role_arn
  outbox_lambda_role_arn       = module.iam.outbox_lambda_role_arn
  dispatch_queue_arn           = module.sqs.dispatch_queue_arn
  dispatch_queue_url           = module.sqs.dispatch_queue_url
  result_queue_arn             = module.sqs.result_queue_arn
  result_queue_url             = module.sqs.result_queue_url
  dynamodb_table_name          = module.dynamodb.table_name
  worker_dedupe_table_name     = module.dynamodb.dedupe_table_name
  dynamodb_gsi_name            = module.dynamodb.gsi_name
  prompts_bucket_name          = module.s3.prompts_bucket_name
  results_bucket_name          = module.s3.results_bucket_name
  provider_lambda_package_path = var.provider_lambda_package_path
  result_lambda_package_path   = var.result_lambda_package_path
  outbox_lambda_package_path   = var.outbox_lambda_package_path
}

# T-8.2 – T-8.6: CloudWatch alarms and SNS
module "monitoring" {
  source = "../../modules/monitoring"

  environment             = var.environment
  alb_arn_suffix          = module.ecs_service.alb_arn_suffix
  target_group_arn_suffix = module.ecs_service.api_target_group_arn_suffix
  ecs_cluster_name        = module.ecs_service.cluster_name
  ecs_api_service_name    = module.ecs_service.api_service_name
  sqs_dlq_name            = module.sqs.dlq_name
  dynamodb_table_name     = module.dynamodb.table_name
  alarm_email             = var.alarm_email
}
