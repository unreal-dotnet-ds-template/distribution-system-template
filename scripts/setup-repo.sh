#!/usr/bin/env bash
# setup-repo.sh
#
# Run this once after creating a new repository from this template.
# Requires the GitHub CLI (gh) to be installed and authenticated.
#
# Usage:
#   bash scripts/setup-repo.sh
#   bash scripts/setup-repo.sh --repo owner/repo-name   # target a specific repo

set -euo pipefail

REPO="${1:-}"

if [ -z "$REPO" ]; then
  REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || true)
fi

if [ -z "$REPO" ]; then
  echo "❌  Could not determine repository. Run inside a git repo or pass --repo owner/name."
  exit 1
fi

echo "🔒  Configuring branch protection for: $REPO (branch: main)"

# ── Classic Branch Protection Rule ────────────────────────────────────────────
# Requires:
#   • Pull request with at least 1 approval before merging
#   • PR Build Pipeline status check to pass
#   • No direct pushes — even by admins
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  "/repos/${REPO}/branches/main/protection" \
  --input - <<EOF
{
  "required_status_checks": {
    "strict": true,
    "checks": [
      { "context": "Build & Test", "app_id": -1 }
    ]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 1,
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": true
  },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "block_creations": false
}
EOF

echo "✅  Branch protection applied."
echo ""
echo "Rules on 'main':"
echo "  • Direct pushes blocked (including admins)"
echo "  • PR required with ≥1 approval"
echo "  • PR Build Pipeline must pass"
echo "  • Stale reviews dismissed on new commits"
echo "  • CODEOWNERS review required"
