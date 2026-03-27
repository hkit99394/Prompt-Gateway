# Terraform state backend - S3 with native lockfile locking

terraform {
  backend "s3" {
    bucket       = "prompt-gateway-terraform-state-prod"
    key          = "prod/terraform.tfstate"
    region       = "us-east-1"
    use_lockfile = true
    encrypt      = true
  }
}
