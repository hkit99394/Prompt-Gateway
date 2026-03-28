variable "aws_region" {
  description = "AWS region for the provider configuration"
  type        = string
  default     = "us-east-1"
}

variable "github_repository" {
  description = "GitHub repository in OWNER/REPO form"
  type        = string
  default     = "hkit99394/Prompt-Gateway"
}

variable "manage_oidc_provider" {
  description = "Whether this stack should create the shared GitHub OIDC provider"
  type        = bool
  default     = true
}

variable "github_thumbprint" {
  description = "GitHub OIDC TLS thumbprint kept for Terraform compatibility"
  type        = string
  default     = "6938fd4d98bab03faadb97b34396831e3780aea1"
}

variable "role_configs" {
  description = "Deploy roles keyed by a logical environment name"
  type = map(object({
    github_environment = string
    role_name          = string
  }))

  default = {
    dev = {
      github_environment = "dev"
      role_name          = "prompt-gateway-github-actions-dev"
    }
    staging = {
      github_environment = "staging"
      role_name          = "prompt-gateway-github-actions-staging"
    }
    prod = {
      github_environment = "prod"
      role_name          = "prompt-gateway-github-actions-prod"
    }
  }
}

variable "tags" {
  description = "Common tags applied to bootstrap resources"
  type        = map(string)
  default = {
    Project   = "Prompt Gateway"
    ManagedBy = "Terraform"
    Scope     = "bootstrap"
  }
}
