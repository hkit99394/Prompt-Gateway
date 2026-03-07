# Terraform state backend - S3 + DynamoDB for locking

terraform {
  backend "s3" {
    bucket         = "prompt-gateway-terraform-state-staging"
    key            = "staging/terraform.tfstate"
    region         = "us-east-1"
    dynamodb_table = "prompt-gateway-terraform-locks-staging"
    encrypt        = true
  }
}
