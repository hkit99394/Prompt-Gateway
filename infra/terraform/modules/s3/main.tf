# S3 module - prompts and results buckets
# Implementation: T-2.7.x

data "aws_caller_identity" "current" {}

locals {
  account_id          = data.aws_caller_identity.current.account_id
  prompts_bucket_name = "${var.bucket_name_prefix}-${var.environment}-prompts-${local.account_id}"
  results_bucket_name = "${var.bucket_name_prefix}-${var.environment}-results-${local.account_id}"
}

# T-2.7.1: Prompts bucket (versioning, encryption)
resource "aws_s3_bucket" "prompts" {
  bucket = local.prompts_bucket_name

  tags = {
    Name        = local.prompts_bucket_name
    Environment = var.environment
  }
}

resource "aws_s3_bucket_versioning" "prompts" {
  bucket = aws_s3_bucket.prompts.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "prompts" {
  bucket = aws_s3_bucket.prompts.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
    bucket_key_enabled = true
  }
}

# T-2.7.2: Results bucket (versioning, encryption)
resource "aws_s3_bucket" "results" {
  bucket = local.results_bucket_name

  tags = {
    Name        = local.results_bucket_name
    Environment = var.environment
  }
}

resource "aws_s3_bucket_versioning" "results" {
  bucket = aws_s3_bucket.results.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "results" {
  bucket = aws_s3_bucket.results.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
    bucket_key_enabled = true
  }
}
