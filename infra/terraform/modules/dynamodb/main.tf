# DynamoDB module - single table, GSI, TTL
# Implementation: T-2.3.x

# T-2.3.1: Table with pk (HASH) and sk (RANGE)
# T-2.3.2: GSI gsi1pk (HASH), gsi1sk (RANGE) for job listing
# T-2.3.3: TTL on attribute ttl
# T-2.3.4: Billing mode (on-demand or provisioned)
# T-2.3.5: Server-side encryption

resource "aws_dynamodb_table" "main" {
  name         = var.table_name
  billing_mode = var.billing_mode

  hash_key  = "pk"
  range_key = "sk"

  dynamic "attribute" {
    for_each = [
      { name = "pk", type = "S" },
      { name = "sk", type = "S" },
      { name = "gsi1pk", type = "S" },
      { name = "gsi1sk", type = "S" }
    ]
    content {
      name = attribute.value.name
      type = attribute.value.type
    }
  }

  # T-2.3.2: GSI for job listing (ListAsync uses gsi1pk/gsi1sk)
  global_secondary_index {
    name            = var.gsi_name
    hash_key        = "gsi1pk"
    range_key       = "gsi1sk"
    projection_type = "ALL"
  }

  # T-2.3.3: TTL for job/event/outbox cleanup
  ttl {
    attribute_name = "ttl"
    enabled        = true
  }

  # T-2.3.5: Server-side encryption (AWS managed key)
  server_side_encryption {
    enabled = true
  }

  tags = {
    Name        = var.table_name
    Environment = var.environment
  }
}

# Provider Worker deduplication table (single hash key: id)
resource "aws_dynamodb_table" "worker_dedupe" {
  name         = var.dedupe_table_name
  billing_mode = var.billing_mode

  hash_key = "id"

  attribute {
    name = "id"
    type = "S"
  }

  ttl {
    attribute_name = "expires_at"
    enabled        = true
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Name        = var.dedupe_table_name
    Environment = var.environment
  }
}
