# Network module - VPC, subnets, security groups
# Implementation: T-2.2.x

data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  azs = slice(data.aws_availability_zones.available.names, 0, 2)
}

# T-2.2.1: VPC
resource "aws_vpc" "main" {
  cidr_block           = var.vpc_cidr
  enable_dns_hostnames = true
  enable_dns_support   = true

  tags = {
    Name        = "prompt-gateway-${var.environment}"
    Environment = var.environment
  }
}

# T-2.2.2: Public subnets (for ALB)
resource "aws_subnet" "public" {
  count                   = length(local.azs)
  vpc_id                  = aws_vpc.main.id
  cidr_block              = cidrsubnet(var.vpc_cidr, 4, count.index)
  availability_zone       = local.azs[count.index]
  map_public_ip_on_launch = true

  tags = {
    Name        = "prompt-gateway-${var.environment}-public-${local.azs[count.index]}"
    Environment = var.environment
  }
}

# T-2.2.3: Private subnets (for ECS tasks)
resource "aws_subnet" "private" {
  count             = length(local.azs)
  vpc_id            = aws_vpc.main.id
  cidr_block        = cidrsubnet(var.vpc_cidr, 4, count.index + 10)
  availability_zone = local.azs[count.index]

  tags = {
    Name        = "prompt-gateway-${var.environment}-private-${local.azs[count.index]}"
    Environment = var.environment
  }
}

# T-2.2.4: Internet Gateway
resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id

  tags = {
    Name        = "prompt-gateway-${var.environment}"
    Environment = var.environment
  }
}

# T-2.2.5: NAT Gateway(s)
resource "aws_eip" "nat" {
  count  = var.single_nat_gateway ? 1 : length(local.azs)
  domain = "vpc"

  tags = {
    Name        = "prompt-gateway-${var.environment}-nat-eip-${count.index}"
    Environment = var.environment
  }

  depends_on = [aws_internet_gateway.main]
}

resource "aws_nat_gateway" "main" {
  count         = var.single_nat_gateway ? 1 : length(local.azs)
  allocation_id = aws_eip.nat[count.index].id
  subnet_id     = aws_subnet.public[count.index].id

  tags = {
    Name        = "prompt-gateway-${var.environment}-nat-${count.index}"
    Environment = var.environment
  }

  depends_on = [aws_internet_gateway.main]
}

# T-2.2.6: Route tables
resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.main.id
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-public"
    Environment = var.environment
  }
}

resource "aws_route_table_association" "public" {
  count          = length(local.azs)
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

resource "aws_route_table" "private" {
  count  = var.single_nat_gateway ? 1 : length(local.azs)
  vpc_id = aws_vpc.main.id

  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.main[var.single_nat_gateway ? 0 : count.index].id
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-private-${count.index}"
    Environment = var.environment
  }
}

resource "aws_route_table_association" "private" {
  count          = length(local.azs)
  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = aws_route_table.private[var.single_nat_gateway ? 0 : count.index].id
}

# T-2.2.7: Security group for ALB (443 inbound, outbound all)
resource "aws_security_group" "alb" {
  name        = "prompt-gateway-${var.environment}-alb"
  description = "ALB security group - HTTPS inbound"
  vpc_id      = aws_vpc.main.id

  ingress {
    description = "HTTPS"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    description = "HTTP (redirect to HTTPS)"
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    description = "All outbound"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-alb"
    Environment = var.environment
  }
}

# T-2.2.8: Security group for ECS API (inbound from ALB only)
resource "aws_security_group" "ecs_api" {
  name        = "prompt-gateway-${var.environment}-ecs-api"
  description = "ECS API tasks - inbound from ALB"
  vpc_id      = aws_vpc.main.id

  ingress {
    description     = "From ALB"
    from_port       = 8080
    to_port         = 8080
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }

  egress {
    description = "All outbound"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-ecs-api"
    Environment = var.environment
  }
}

# T-2.2.9: Security group for ECS worker (outbound to SQS, S3, OpenAI)
resource "aws_security_group" "ecs_worker" {
  name        = "prompt-gateway-${var.environment}-ecs-worker"
  description = "ECS worker tasks - outbound to SQS, S3, OpenAI"
  vpc_id      = aws_vpc.main.id

  egress {
    description = "All outbound (SQS, S3, OpenAI API)"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name        = "prompt-gateway-${var.environment}-ecs-worker"
    Environment = var.environment
  }
}
