# SQS module variables
# Implementation: T-2.4.x

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "queue_name_prefix" {
  description = "Prefix for queue names"
  type        = string
  default     = "prompt-gateway"
}

# T-2.4.4: Visibility timeout (worker processing time)
variable "visibility_timeout_seconds" {
  description = "Visibility timeout for messages (worker processing)"
  type        = number
  default     = 300
}

# T-2.4.5: Message retention
variable "message_retention_seconds" {
  description = "Message retention period (14 days default)"
  type        = number
  default     = 1209600 # 14 days
}

variable "max_receive_count" {
  description = "Max receive count before redrive to DLQ"
  type        = number
  default     = 3
}
