#!/usr/bin/env bash
# Package the Lambda projects into the zip artifacts expected by Terraform.
#
# Output files:
#   artifacts/provider-worker-lambda.zip
#   artifacts/control-plane-result-lambda.zip
#   artifacts/control-plane-outbox-lambda.zip
#
# Usage:
#   ./scripts/package-lambda-artifacts.sh
#   CONFIGURATION=Release ./scripts/package-lambda-artifacts.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ARTIFACTS_DIR="$REPO_ROOT/artifacts"
CONFIGURATION="${CONFIGURATION:-Release}"

if ! command -v zip >/dev/null 2>&1; then
  echo "Error: zip is required but was not found in PATH."
  exit 1
fi

mkdir -p "$ARTIFACTS_DIR"

package_project() {
  local project_path="$1"
  local output_zip="$2"
  local project_name="$3"

  local publish_dir
  publish_dir="$(mktemp -d "${TMPDIR:-/tmp}/lambda-publish.XXXXXX")"

  echo ""
  echo "Packaging $project_name"
  dotnet publish "$project_path" -c "$CONFIGURATION" -o "$publish_dir"

  rm -f "$output_zip"
  (
    cd "$publish_dir"
    zip -qr "$output_zip" .
  )

  rm -rf "$publish_dir"
  echo "  Wrote $output_zip"
}

echo "=== Packaging Lambda artifacts ==="
echo "Configuration: $CONFIGURATION"
echo "Artifacts dir: $ARTIFACTS_DIR"

package_project \
  "$REPO_ROOT/Prompt Gateway Provider - OpenAI/src/Provider.Worker.Lambda/Provider.Worker.Lambda.csproj" \
  "$ARTIFACTS_DIR/provider-worker-lambda.zip" \
  "Provider Worker Lambda"

package_project \
  "$REPO_ROOT/Prompt Gateway – Control Plane /src/ControlPlane.ResultLambda/ControlPlane.ResultLambda.csproj" \
  "$ARTIFACTS_DIR/control-plane-result-lambda.zip" \
  "Control Plane Result Lambda"

package_project \
  "$REPO_ROOT/Prompt Gateway – Control Plane /src/ControlPlane.OutboxLambda/ControlPlane.OutboxLambda.csproj" \
  "$ARTIFACTS_DIR/control-plane-outbox-lambda.zip" \
  "Control Plane Outbox Lambda"

echo ""
echo "Done. Terraform defaults now point at:"
echo "  artifacts/provider-worker-lambda.zip"
echo "  artifacts/control-plane-result-lambda.zip"
echo "  artifacts/control-plane-outbox-lambda.zip"
