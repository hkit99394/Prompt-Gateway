# SQS module - dispatch, result, DLQ queues
# Implementation: T-2.4.x

locals {
  name_prefix = "${var.queue_name_prefix}-${var.environment}"
}

# T-2.4.1: DLQ (dead-letter queue)
# For same-account, redrive_policy on source queues is sufficient; no redrive_allow_policy needed
resource "aws_sqs_queue" "dlq" {
  name = "${local.name_prefix}-dlq"

  message_retention_seconds = var.message_retention_seconds

  tags = {
    Name        = "${local.name_prefix}-dlq"
    Environment = var.environment
  }
}

# T-2.4.2: Dispatch queue (Control Plane → Provider Worker)
resource "aws_sqs_queue" "dispatch" {
  name = "${local.name_prefix}-dispatch"

  visibility_timeout_seconds = var.visibility_timeout_seconds
  message_retention_seconds = var.message_retention_seconds

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.dlq.arn
    maxReceiveCount     = var.max_receive_count
  })

  tags = {
    Name        = "${local.name_prefix}-dispatch"
    Environment = var.environment
  }
}

# T-2.4.3: Result queue (Provider Worker → Control Plane)
resource "aws_sqs_queue" "result" {
  name = "${local.name_prefix}-result"

  visibility_timeout_seconds = var.visibility_timeout_seconds
  message_retention_seconds  = var.message_retention_seconds

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.dlq.arn
    maxReceiveCount     = var.max_receive_count
  })

  tags = {
    Name        = "${local.name_prefix}-result"
    Environment = var.environment
  }
}
