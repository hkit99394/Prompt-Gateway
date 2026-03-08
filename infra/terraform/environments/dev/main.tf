# Dev environment - root module
# Calls all infrastructure modules with dev-specific configuration
# Implementation: T-2.8.1

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
}

module "network" {
  source = "../../modules/network"

  environment        = var.environment
  vpc_cidr           = "10.0.0.0/16"
  single_nat_gateway = true
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

  environment                  = var.environment
  vpc_id                       = module.network.vpc_id
  private_subnet_ids           = module.network.private_subnet_ids
  public_subnet_ids            = module.network.public_subnet_ids
  alb_security_group_id        = module.network.alb_security_group_id
  ecs_api_security_group_id    = module.network.ecs_api_security_group_id
  ecs_worker_security_group_id = module.network.ecs_worker_security_group_id
  ecs_execution_control_plane_role_arn   = module.iam.ecs_execution_control_plane_role_arn
  ecs_execution_provider_worker_role_arn = module.iam.ecs_execution_provider_worker_role_arn
  control_plane_task_role_arn  = module.iam.control_plane_task_role_arn
  provider_worker_task_role_arn = module.iam.provider_worker_task_role_arn
  dynamodb_table_name          = module.dynamodb.table_name
  worker_dedupe_table_name     = module.dynamodb.dedupe_table_name
  dynamodb_gsi_name            = module.dynamodb.gsi_name
  dispatch_queue_url           = module.sqs.dispatch_queue_url
  result_queue_url             = module.sqs.result_queue_url
  prompts_bucket_name          = module.s3.prompts_bucket_name
  results_bucket_name          = module.s3.results_bucket_name
  api_desired_count            = 1
  worker_desired_count         = 1
  api_cpu                      = 256
  api_memory                   = 512
  worker_cpu                   = 256
  worker_memory                = 512
  # certificate_arn omitted - dev uses HTTP on port 80
}
