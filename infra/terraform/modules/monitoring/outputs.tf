# Monitoring module outputs

output "alarm_topic_arn" {
  description = "SNS topic ARN for alarm notifications (add PagerDuty or other subscriptions as needed)"
  value       = aws_sns_topic.alarms.arn
}

output "alarm_topic_name" {
  description = "SNS topic name"
  value       = aws_sns_topic.alarms.name
}
