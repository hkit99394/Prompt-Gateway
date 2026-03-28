# Terraform state backend - S3 with native lockfile locking
# Configure these before first terraform init:
#   - Create S3 bucket: prompt-gateway-terraform-state-dev
#   - Or use -backend-config=backend.hcl to override

terraform {
  backend "s3" {
    bucket       = "prompt-gateway-terraform-state-dev"
    key          = "dev/terraform.tfstate"
    region       = "us-east-1"
    use_lockfile = true
    encrypt      = true
  }
}
