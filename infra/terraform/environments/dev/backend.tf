# Terraform state backend - S3 + DynamoDB for locking
# Configure these before first terraform init:
#   - Create S3 bucket: prompt-gateway-terraform-state-dev
#   - Create DynamoDB table: prompt-gateway-terraform-locks-dev
#   - Or use -backend-config=backend.hcl to override

terraform {
  backend "s3" {
    bucket         = "prompt-gateway-terraform-state-dev"
    key            = "dev/terraform.tfstate"
    region         = "us-east-1"
    dynamodb_table = "prompt-gateway-terraform-locks-dev"
    encrypt        = true
  }
}
