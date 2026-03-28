#!/usr/bin/env bash
# Bootstrap Terraform backend - S3 bucket for Terraform state with native lockfile locking
# Run this once before first terraform init (T-5.1.2)
#
# Usage: ./scripts/bootstrap-terraform-backend.sh [dev|staging|prod]
#
# Creates:
#   - S3 bucket: prompt-gateway-terraform-state-{env}

set -euo pipefail

ENV="${1:-dev}"
REGION="${AWS_REGION:-us-east-1}"
BUCKET="prompt-gateway-terraform-state-${ENV}"
PROJECT_TAG_VALUE="Prompt Gateway"

echo "Bootstrap Terraform backend for environment: $ENV"
echo "  S3 bucket: $BUCKET"
echo "  Region: $REGION"
echo ""

# Create S3 bucket if it doesn't exist
if aws s3api head-bucket --bucket "$BUCKET" 2>/dev/null; then
  echo "S3 bucket $BUCKET already exists"
else
  echo "Creating S3 bucket $BUCKET..."
  if [ "$REGION" = "us-east-1" ]; then
    aws s3api create-bucket --bucket "$BUCKET"
  else
    aws s3api create-bucket --bucket "$BUCKET" \
      --create-bucket-configuration "LocationConstraint=$REGION"
  fi
  aws s3api put-bucket-versioning \
    --bucket "$BUCKET" \
    --versioning-configuration Status=Enabled
  aws s3api put-bucket-encryption \
    --bucket "$BUCKET" \
    --server-side-encryption-configuration '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}'
  echo "  Created S3 bucket $BUCKET"
fi

# Ensure S3 bucket tags are set
aws s3api put-bucket-tagging \
  --bucket "$BUCKET" \
  --tagging "{\"TagSet\":[{\"Key\":\"Project\",\"Value\":\"${PROJECT_TAG_VALUE}\"},{\"Key\":\"Environment\",\"Value\":\"${ENV}\"}]}"

echo ""
echo "Backend bootstrap complete. Run:"
echo "  cd infra/terraform/environments/$ENV"
echo "  terraform init"
echo "  terraform plan -var-file=${ENV}.tfvars"
echo "  terraform apply -var-file=${ENV}.tfvars"
