#!/usr/bin/env bash
set -euo pipefail
# Block destructive commands — customize this blocklist for your repo
BLOCKED_PATTERNS=("rm -rf /" "DROP DATABASE" "format C:" "mkfs" "git push --force")
for pattern in "${BLOCKED_PATTERNS[@]}"; do
  if echo "$*" | grep -qi "$pattern"; then
    echo "❌ Blocked: destructive pattern detected ($pattern)" >&2
    exit 1
  fi
done
