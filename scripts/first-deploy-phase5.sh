#!/usr/bin/env bash
# First-deploy Phase 5: Lambda cutover
# Backward-compatible wrapper around scripts/set-processing-mode.sh for teams that
# still use the Phase 5 entry point directly.
#
# Usage: ./scripts/first-deploy-phase5.sh [--plan-only] [--skip-package] [--skip-verify]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

exec "$REPO_ROOT/scripts/set-processing-mode.sh" --mode lambda "$@"
