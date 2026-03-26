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
