# Network module variables
# Implementation: T-2.2.x

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
}

variable "vpc_cidr" {
  description = "CIDR block for VPC"
  type        = string
  default     = "10.0.0.0/16"
}

variable "single_nat_gateway" {
  description = "Use single NAT gateway (dev cost savings) vs one per AZ"
  type        = bool
  default     = true
}
