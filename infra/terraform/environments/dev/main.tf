# Dev environment - root module
# Calls all infrastructure modules with dev-specific configuration
# Implementation: T-2.7.1

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

# Module calls will be added in T-2.7.x
# module "network" { ... }
# module "dynamodb" { ... }
# module "sqs" { ... }
# module "iam" { ... }
# module "ecs_service" { ... }
