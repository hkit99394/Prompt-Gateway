provider "aws" {
  region = var.aws_region

  default_tags {
    tags = var.tags
  }
}

data "aws_caller_identity" "current" {}

data "aws_iam_policy_document" "github_actions_assume_role" {
  for_each = var.role_configs

  statement {
    effect = "Allow"

    actions = [
      "sts:AssumeRoleWithWebIdentity"
    ]

    principals {
      type        = "Federated"
      identifiers = [local.oidc_provider_arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repository}:environment:${each.value.github_environment}"]
    }
  }
}

data "aws_iam_policy_document" "github_actions_deploy_iam" {
  for_each = var.role_configs

  statement {
    sid    = "ReadIamMetadata"
    effect = "Allow"

    actions = [
      "iam:GetPolicy",
      "iam:GetPolicyVersion",
      "iam:GetRole",
      "iam:GetRolePolicy",
      "iam:ListAttachedRolePolicies",
      "iam:ListPolicyVersions",
      "iam:ListRolePolicies",
      "iam:ListRoleTags",
      "iam:SimulatePrincipalPolicy"
    ]

    resources = ["*"]
  }

  statement {
    sid    = "PassEnvironmentRuntimeRolesOnly"
    effect = "Allow"

    actions = [
      "iam:PassRole"
    ]

    resources = local.runtime_role_arns_by_env[each.key]

    condition {
      test     = "StringEqualsIfExists"
      variable = "iam:PassedToService"
      values = [
        "ecs-tasks.amazonaws.com",
        "lambda.amazonaws.com"
      ]
    }
  }

}

locals {
  oidc_provider_arn = var.manage_oidc_provider ? aws_iam_openid_connect_provider.github[0].arn : "arn:aws:iam::${data.aws_caller_identity.current.account_id}:oidc-provider/token.actions.githubusercontent.com"
  runtime_role_names_by_env = {
    for key, config in var.role_configs : key => [
      "prompt-gateway-${config.github_environment}-ecs-execution-api",
      "prompt-gateway-${config.github_environment}-ecs-execution-worker",
      "prompt-gateway-${config.github_environment}-control-plane-api",
      "prompt-gateway-${config.github_environment}-provider-worker",
      "prompt-gateway-${config.github_environment}-provider-worker-lambda",
      "prompt-gateway-${config.github_environment}-result-ingestion-lambda",
      "prompt-gateway-${config.github_environment}-outbox-dispatch-lambda",
      "prompt-gateway-${config.github_environment}-control-plane-http-lambda",
      "prompt-gateway-${config.github_environment}-control-plane-http-authorizer"
    ]
  }
  runtime_role_arns_by_env = {
    for key, role_names in local.runtime_role_names_by_env : key => [
      for role_name in role_names : "arn:aws:iam::${data.aws_caller_identity.current.account_id}:role/${role_name}"
    ]
  }
}

resource "aws_iam_openid_connect_provider" "github" {
  count = var.manage_oidc_provider ? 1 : 0

  url = "https://token.actions.githubusercontent.com"

  client_id_list = [
    "sts.amazonaws.com"
  ]

  # GitHub notes that AWS no longer needs the thumbprint, but the Terraform
  # provider still expects one for this resource shape.
  thumbprint_list = [
    var.github_thumbprint
  ]
}

resource "aws_iam_role" "github_actions" {
  for_each = var.role_configs

  name               = each.value.role_name
  assume_role_policy = data.aws_iam_policy_document.github_actions_assume_role[each.key].json

  tags = merge(var.tags, {
    Name              = each.value.role_name
    GitHubEnvironment = each.value.github_environment
  })
}

resource "aws_iam_role_policy_attachment" "power_user" {
  for_each = var.role_configs

  role       = aws_iam_role.github_actions[each.key].name
  policy_arn = "arn:aws:iam::aws:policy/PowerUserAccess"
}

resource "aws_iam_role_policy" "deploy_iam" {
  for_each = var.role_configs

  name   = "prompt-gateway-github-actions-deploy"
  role   = aws_iam_role.github_actions[each.key].id
  policy = data.aws_iam_policy_document.github_actions_deploy_iam[each.key].json
}
