# Monitoring module - CloudWatch alarms and SNS (T-8.2 – T-8.6)

locals {
  name_prefix = "prompt-gateway-${var.environment}"
}

# T-8.6: SNS topic for alarm notifications
resource "aws_sns_topic" "alarms" {
  name = "${local.name_prefix}-alarms"

  tags = {
    Name        = "${local.name_prefix}-alarms"
    Environment = var.environment
  }
}

resource "aws_sns_topic_subscription" "alarm_email" {
  count = var.alarm_email != "" ? 1 : 0

  topic_arn = aws_sns_topic.alarms.arn
  protocol  = "email"
  endpoint  = var.alarm_email
}

# T-8.2: API 5xx rate alarm (ALB target group)
resource "aws_cloudwatch_metric_alarm" "api_5xx" {
  alarm_name          = "${local.name_prefix}-api-5xx"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = var.api_5xx_evaluation_periods
  metric_name         = "HTTPCode_Target_5XX_Count"
  namespace           = "AWS/ApplicationELB"
  period              = var.api_5xx_period_seconds
  statistic           = "Sum"
  threshold           = var.api_5xx_threshold
  alarm_description   = "Control Plane API target is returning 5xx responses"
  treat_missing_data  = "notBreaching"

  dimensions = {
    LoadBalancer = var.alb_arn_suffix
    TargetGroup  = var.target_group_arn_suffix
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = {
    Name        = "${local.name_prefix}-api-5xx"
    Environment = var.environment
  }
}

# T-8.3: ECS API service CPU utilization
resource "aws_cloudwatch_metric_alarm" "ecs_api_cpu" {
  alarm_name          = "${local.name_prefix}-ecs-api-cpu"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 2
  metric_name         = "CPUUtilization"
  namespace           = "AWS/ECS"
  period              = 300
  statistic           = "Average"
  threshold           = var.ecs_cpu_threshold_percent
  alarm_description   = "Control Plane API ECS service CPU utilization is high"
  treat_missing_data  = "notBreaching"

  dimensions = {
    ClusterName = var.ecs_cluster_name
    ServiceName = var.ecs_api_service_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = {
    Name        = "${local.name_prefix}-ecs-api-cpu"
    Environment = var.environment
  }
}

# T-8.3: ECS API service memory utilization
resource "aws_cloudwatch_metric_alarm" "ecs_api_memory" {
  alarm_name          = "${local.name_prefix}-ecs-api-memory"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 2
  metric_name         = "MemoryUtilization"
  namespace           = "AWS/ECS"
  period              = 300
  statistic           = "Average"
  threshold           = var.ecs_memory_threshold_percent
  alarm_description   = "Control Plane API ECS service memory utilization is high"
  treat_missing_data  = "notBreaching"

  dimensions = {
    ClusterName = var.ecs_cluster_name
    ServiceName = var.ecs_api_service_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = {
    Name        = "${local.name_prefix}-ecs-api-memory"
    Environment = var.environment
  }
}

# T-8.4: SQS DLQ message count > 0
resource "aws_cloudwatch_metric_alarm" "sqs_dlq_messages" {
  alarm_name          = "${local.name_prefix}-sqs-dlq-messages"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 300
  statistic           = "Sum"
  threshold           = 0
  alarm_description   = "Messages are in the dead-letter queue; investigate failed dispatch or result processing"
  treat_missing_data  = "notBreaching"

  dimensions = {
    QueueName = var.sqs_dlq_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = {
    Name        = "${local.name_prefix}-sqs-dlq-messages"
    Environment = var.environment
  }
}

resource "aws_cloudwatch_metric_alarm" "dispatch_queue_visible_messages" {
  alarm_name          = "${local.name_prefix}-dispatch-queue-visible"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 300
  statistic           = "Maximum"
  threshold           = var.dispatch_queue_visible_threshold
  alarm_description   = "Dispatch queue has a visible backlog that may indicate provider dispatch pressure"
  treat_missing_data  = "notBreaching"

  dimensions = {
    QueueName = var.dispatch_queue_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = {
    Name        = "${local.name_prefix}-dispatch-queue-visible"
    Environment = var.environment
  }
}

resource "aws_cloudwatch_metric_alarm" "dispatch_queue_oldest_age" {
  alarm_name          = "${local.name_prefix}-dispatch-queue-age"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateAgeOfOldestMessage"
  namespace           = "AWS/SQS"
  period              = 300
  statistic           = "Maximum"
  threshold           = var.dispatch_queue_age_threshold_seconds
  alarm_description   = "Dispatch queue oldest message age is elevated, which suggests consumer lag"
  treat_missing_data  = "notBreaching"

  dimensions = {
    QueueName = var.dispatch_queue_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = {
    Name        = "${local.name_prefix}-dispatch-queue-age"
    Environment = var.environment
  }
}

resource "aws_cloudwatch_metric_alarm" "result_queue_visible_messages" {
  alarm_name          = "${local.name_prefix}-result-queue-visible"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateNumberOfMessagesVisible"
  namespace           = "AWS/SQS"
  period              = 300
  statistic           = "Maximum"
  threshold           = var.result_queue_visible_threshold
  alarm_description   = "Result queue has a visible backlog that may indicate ingestion pressure"
  treat_missing_data  = "notBreaching"

  dimensions = {
    QueueName = var.result_queue_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = {
    Name        = "${local.name_prefix}-result-queue-visible"
    Environment = var.environment
  }
}

resource "aws_cloudwatch_metric_alarm" "result_queue_oldest_age" {
  alarm_name          = "${local.name_prefix}-result-queue-age"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "ApproximateAgeOfOldestMessage"
  namespace           = "AWS/SQS"
  period              = 300
  statistic           = "Maximum"
  threshold           = var.result_queue_age_threshold_seconds
  alarm_description   = "Result queue oldest message age is elevated, which suggests ingestion lag"
  treat_missing_data  = "notBreaching"

  dimensions = {
    QueueName = var.result_queue_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = {
    Name        = "${local.name_prefix}-result-queue-age"
    Environment = var.environment
  }
}

resource "aws_cloudwatch_metric_alarm" "provider_lambda_errors" {
  count = var.provider_lambda_function_name != null ? 1 : 0

  alarm_name          = "${local.name_prefix}-provider-lambda-errors"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Errors"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Sum"
  threshold           = 0
  alarm_description   = "Provider worker Lambda is returning errors"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.provider_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "provider_lambda_throttles" {
  count = var.provider_lambda_function_name != null ? 1 : 0

  alarm_name          = "${local.name_prefix}-provider-lambda-throttles"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Throttles"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Sum"
  threshold           = 0
  alarm_description   = "Provider worker Lambda is being throttled"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.provider_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "provider_lambda_duration" {
  count = var.provider_lambda_function_name != null ? 1 : 0

  alarm_name          = "${local.name_prefix}-provider-lambda-duration"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Duration"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Maximum"
  threshold           = max(1000, floor(var.provider_lambda_timeout_seconds * 1000 * 0.8))
  alarm_description   = "Provider worker Lambda duration is approaching its timeout budget"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.provider_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "provider_lambda_concurrency" {
  count = var.provider_lambda_function_name != null && var.provider_lambda_reserved_concurrency > 0 ? 1 : 0

  alarm_name          = "${local.name_prefix}-provider-lambda-concurrency"
  comparison_operator = "GreaterThanOrEqualToThreshold"
  evaluation_periods  = 1
  metric_name         = "ConcurrentExecutions"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Maximum"
  threshold           = max(1, floor(var.provider_lambda_reserved_concurrency * 0.8))
  alarm_description   = "Provider worker Lambda concurrency is near its reserved limit"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.provider_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "result_lambda_errors" {
  count = var.result_lambda_function_name != null ? 1 : 0

  alarm_name          = "${local.name_prefix}-result-lambda-errors"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Errors"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Sum"
  threshold           = 0
  alarm_description   = "Result ingestion Lambda is returning errors"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.result_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "result_lambda_throttles" {
  count = var.result_lambda_function_name != null ? 1 : 0

  alarm_name          = "${local.name_prefix}-result-lambda-throttles"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Throttles"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Sum"
  threshold           = 0
  alarm_description   = "Result ingestion Lambda is being throttled"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.result_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "result_lambda_duration" {
  count = var.result_lambda_function_name != null ? 1 : 0

  alarm_name          = "${local.name_prefix}-result-lambda-duration"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Duration"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Maximum"
  threshold           = max(1000, floor(var.result_lambda_timeout_seconds * 1000 * 0.8))
  alarm_description   = "Result ingestion Lambda duration is approaching its timeout budget"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.result_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "result_lambda_concurrency" {
  count = var.result_lambda_function_name != null && var.result_lambda_reserved_concurrency > 0 ? 1 : 0

  alarm_name          = "${local.name_prefix}-result-lambda-concurrency"
  comparison_operator = "GreaterThanOrEqualToThreshold"
  evaluation_periods  = 1
  metric_name         = "ConcurrentExecutions"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Maximum"
  threshold           = max(1, floor(var.result_lambda_reserved_concurrency * 0.8))
  alarm_description   = "Result ingestion Lambda concurrency is near its reserved limit"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.result_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "outbox_lambda_errors" {
  count = var.outbox_lambda_function_name != null ? 1 : 0

  alarm_name          = "${local.name_prefix}-outbox-lambda-errors"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Errors"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Sum"
  threshold           = 0
  alarm_description   = "Outbox dispatch Lambda is returning errors"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.outbox_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "outbox_lambda_throttles" {
  count = var.outbox_lambda_function_name != null ? 1 : 0

  alarm_name          = "${local.name_prefix}-outbox-lambda-throttles"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Throttles"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Sum"
  threshold           = 0
  alarm_description   = "Outbox dispatch Lambda is being throttled"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.outbox_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "outbox_lambda_duration" {
  count = var.outbox_lambda_function_name != null ? 1 : 0

  alarm_name          = "${local.name_prefix}-outbox-lambda-duration"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "Duration"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Maximum"
  threshold           = max(1000, floor(var.outbox_lambda_timeout_seconds * 1000 * 0.8))
  alarm_description   = "Outbox dispatch Lambda duration is approaching its timeout budget"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.outbox_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

resource "aws_cloudwatch_metric_alarm" "outbox_lambda_concurrency" {
  count = var.outbox_lambda_function_name != null && var.outbox_lambda_reserved_concurrency > 0 ? 1 : 0

  alarm_name          = "${local.name_prefix}-outbox-lambda-concurrency"
  comparison_operator = "GreaterThanOrEqualToThreshold"
  evaluation_periods  = 1
  metric_name         = "ConcurrentExecutions"
  namespace           = "AWS/Lambda"
  period              = 300
  statistic           = "Maximum"
  threshold           = max(1, floor(var.outbox_lambda_reserved_concurrency * 0.8))
  alarm_description   = "Outbox dispatch Lambda concurrency is near its reserved limit"
  treat_missing_data  = "notBreaching"

  dimensions = {
    FunctionName = var.outbox_lambda_function_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]
}

# T-8.5: DynamoDB throttles (write throttle events)
resource "aws_cloudwatch_metric_alarm" "dynamodb_throttles" {
  alarm_name          = "${local.name_prefix}-dynamodb-throttles"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  metric_name         = "WriteThrottleEvents"
  namespace           = "AWS/DynamoDB"
  period              = 300
  statistic           = "Sum"
  threshold           = 0
  alarm_description   = "DynamoDB table is throttling write requests"
  treat_missing_data  = "notBreaching"

  dimensions = {
    TableName = var.dynamodb_table_name
  }

  alarm_actions = [aws_sns_topic.alarms.arn]
  ok_actions    = [aws_sns_topic.alarms.arn]

  tags = {
    Name        = "${local.name_prefix}-dynamodb-throttles"
    Environment = var.environment
  }
}
