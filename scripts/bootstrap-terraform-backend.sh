#!/usr/bin/env bash
# Bootstrap Terraform backend - S3 bucket + DynamoDB table for state locking
# Run this once before first terraform init (T-5.1.2)
#
# Usage: ./scripts/bootstrap-terraform-backend.sh [dev|staging|prod]
#
# Creates:
#   - S3 bucket: prompt-gateway-terraform-state-{env}
#   - DynamoDB table: prompt-gateway-terraform-locks-{env}

set -euo pipefail

ENV="${1:-dev}"
REGION="${AWS_REGION:-us-east-1}"
BUCKET="prompt-gateway-terraform-state-${ENV}"
TABLE="prompt-gateway-terraform-locks-${ENV}"
PROJECT_TAG_VALUE="Prompt Gateway"

echo "Bootstrap Terraform backend for environment: $ENV"
echo "  S3 bucket: $BUCKET"
echo "  DynamoDB table: $TABLE"
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

# Create DynamoDB table if it doesn't exist
if aws dynamodb describe-table --table-name "$TABLE" --region "$REGION" 2>/dev/null; then
  echo "DynamoDB table $TABLE already exists"
else
  echo "Creating DynamoDB table $TABLE..."
  aws dynamodb create-table \
    --table-name "$TABLE" \
    --attribute-definitions AttributeName=LockID,AttributeType=S \
    --key-schema AttributeName=LockID,KeyType=HASH \
    --billing-mode PAY_PER_REQUEST \
    --tags "[{\"Key\":\"Project\",\"Value\":\"${PROJECT_TAG_VALUE}\"},{\"Key\":\"Environment\",\"Value\":\"${ENV}\"}]" \
    --region "$REGION"
  echo "  Waiting for table to be active..."
  aws dynamodb wait table-exists --table-name "$TABLE" --region "$REGION"
  echo "  Created DynamoDB table $TABLE"
fi

# Ensure DynamoDB table tags are set
TABLE_ARN=$(aws dynamodb describe-table --table-name "$TABLE" --region "$REGION" --query 'Table.TableArn' --output text)
aws dynamodb tag-resource \
  --resource-arn "$TABLE_ARN" \
  --tags "[{\"Key\":\"Project\",\"Value\":\"${PROJECT_TAG_VALUE}\"},{\"Key\":\"Environment\",\"Value\":\"${ENV}\"}]" \
  --region "$REGION"

echo ""
echo "Backend bootstrap complete. Run:"
echo "  cd infra/terraform/environments/$ENV"
echo "  terraform init"
echo "  terraform plan -var-file=${ENV}.tfvars"
echo "  terraform apply -var-file=${ENV}.tfvars"
