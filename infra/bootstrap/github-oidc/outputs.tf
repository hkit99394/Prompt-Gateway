output "oidc_provider_arn" {
  description = "ARN of the GitHub OIDC provider used by the deploy roles"
  value       = local.oidc_provider_arn
}

output "github_actions_role_arns" {
  description = "Deploy role ARNs keyed by logical environment name"
  value = {
    for key, role in aws_iam_role.github_actions : key => role.arn
  }
}

output "github_actions_role_names" {
  description = "Deploy role names keyed by logical environment name"
  value = {
    for key, role in aws_iam_role.github_actions : key => role.name
  }
}
