# Prompt Gateway GitHub OIDC Bootstrap

Bootstrap stack for GitHub Actions access to AWS using OpenID Connect (OIDC).

This stack is intended to be applied once with an admin-capable AWS session.
After that, GitHub Actions can assume short-lived roles instead of using long-lived AWS keys.

## What it creates

- the GitHub OIDC provider for AWS IAM
- one deploy role per GitHub Environment
- an environment-scoped inline IAM policy that fills the `iam:PassRole` gap left by `PowerUserAccess`

By default, the roles trust this repository:

- `hkit99394/Prompt-Gateway`

And these GitHub Environments:

- `dev`
- `staging`
- `prod`

## Why this stack exists

The repo CD workflows use:

- [cd-dev.yml](/Users/jacktam/Documents/Project/Prompt Gateway/.github/workflows/cd-dev.yml)
- [cd-staging.yml](/Users/jacktam/Documents/Project/Prompt Gateway/.github/workflows/cd-staging.yml)
- [cd-prod.yml](/Users/jacktam/Documents/Project/Prompt Gateway/.github/workflows/cd-prod.yml)

Those workflows authenticate with AWS through `aws-actions/configure-aws-credentials` and a GitHub OIDC role.

The deploy scripts then run Terraform and AWS CLI operations from:

- [first-deploy-phase3.sh](/Users/jacktam/Documents/Project/Prompt Gateway/scripts/first-deploy-phase3.sh)
- [first-deploy-phase4.sh](/Users/jacktam/Documents/Project/Prompt Gateway/scripts/first-deploy-phase4.sh)
- [promote-lambda-mode.sh](/Users/jacktam/Documents/Project/Prompt Gateway/scripts/promote-lambda-mode.sh)

## Usage

```bash
cd infra/bootstrap/github-oidc
terraform init
terraform plan
terraform apply
```

If the AWS account already has the GitHub OIDC provider, either:

- import it into this stack, or
- set `manage_oidc_provider = false`

## Example tfvars

```hcl
github_repository = "hkit99394/Prompt-Gateway"

role_configs = {
  dev = {
    github_environment = "dev"
    role_name          = "prompt-gateway-github-actions-dev"
  }
  staging = {
    github_environment = "staging"
    role_name          = "prompt-gateway-github-actions-staging"
  }
  prod = {
    github_environment = "prod"
    role_name          = "prompt-gateway-github-actions-prod"
  }
}
```

## GitHub setup after apply

Set these GitHub Environment variables:

- `dev` -> `AWS_ROLE_ARN = <dev role arn>`
- `staging` -> `AWS_ROLE_ARN = <staging role arn>`
- `prod` -> `AWS_ROLE_ARN = <prod role arn>`

And for the app workflows:

- `staging` -> `CERTIFICATE_ARN`
- `prod` -> `CERTIFICATE_ARN`
- optional: `ALARM_EMAIL`
- optional: `HEALTH_CHECK_BASE_URL`

## Notes

- The trust policy uses the GitHub OIDC `sub` format `repo:OWNER/REPO:environment:ENV`.
- Because the workflows use GitHub Environments, the trust is environment-scoped rather than branch-scoped.
- The deploy roles attach AWS managed `PowerUserAccess` plus environment-scoped `iam:PassRole` permissions for existing Prompt Gateway runtime roles only.
- This stack intentionally does not grant GitHub Actions permission to create or modify IAM roles or policies.
- Apply IAM topology changes, initial infrastructure bootstrap, and any future IAM module refactors with an admin-capable human session.
