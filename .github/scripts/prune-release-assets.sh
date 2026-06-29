#!/usr/bin/env bash
# Asset retention for the public releases repo. The release entry and its tag are
# always kept (history stays visible on the Releases page); only the heavy download
# assets are pruned. Keep assets on the newest release always and on the 2nd-newest
# while it is within the age cap; strip assets from everything else (3rd onward, or
# past the cap).
#
# Shared by the release workflow (runs after each publish) and a scheduled workflow
# (enforces the age cap independently of new releases). Requires GH_TOKEN in the env.
set -euo pipefail

REPO="${RELEASES_REPO:-KamilBugaj/ai-usage-app-releases}"
MAX_AGE_DAYS="${MAX_AGE_DAYS:-14}"
MAX_AGE=$(( MAX_AGE_DAYS * 24 * 3600 ))
NOW=$(date -u +%s)
failures=0

# High --limit ceiling so a backlog can never hide old releases from cleanup;
# retention keeps the active set tiny in practice. Tab-separated to parse safely.
releases=$(gh release list --repo "$REPO" --limit 1000 \
  --json tagName,createdAt \
  --jq 'sort_by(.createdAt) | reverse | to_entries[] | "\(.key)\t\(.value.tagName)\t\(.value.createdAt)"')

while IFS=$'\t' read -r idx tag created; do
  [ -n "$tag" ] || continue
  age=$(( NOW - $(date -u -d "$created" +%s) ))
  age_days=$(( age / 86400 ))

  if [ "$idx" -eq 0 ]; then
    echo "keep assets (latest): $tag"
    continue
  fi
  if [ "$idx" -eq 1 ] && [ "$age" -le "$MAX_AGE" ]; then
    echo "keep assets (2nd newest, ${age_days}d old): $tag"
    continue
  fi

  echo "strip assets: $tag (idx=$idx, age=${age_days}d)"
  assets=$(gh release view "$tag" --repo "$REPO" --json assets --jq '.assets[].name')
  while read -r asset; do
    [ -n "$asset" ] || continue
    echo "  delete asset: $asset"
    if ! gh release delete-asset "$tag" "$asset" --repo "$REPO" --yes; then
      echo "::warning::failed to delete asset $asset from $tag"
      failures=1
    fi
  done <<< "$assets"
done <<< "$releases"

# Propagate a red build if any asset delete failed (e.g. token scope changed).
exit "$failures"
