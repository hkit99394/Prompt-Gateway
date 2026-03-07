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

module "network" {
  source = "../../modules/network"

  environment        = var.environment
  vpc_cidr           = "10.0.0.0/16"
  single_nat_gateway = true
}
