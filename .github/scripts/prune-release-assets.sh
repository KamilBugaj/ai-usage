#!/usr/bin/env bash
# Asset retention for the public releases repo. The release entry and its tag are
# always kept (history stays visible on the Releases page); only the heavy download
# assets are pruned. Keep assets on the KEEP_NEWEST newest releases OR on anything
# published within the age cap, whichever is more generous; strip the rest.
#
# The two rules are a union, not an intersection. Under the old intersection the
# policy bit hardest exactly when a fallback mattered most: a slow patch cycle aged
# the last good release past the cap, so the next release stripped it on the same
# run and left a single downloadable version. There is no in-app updater, so users
# install by hand from the Releases page and need a version to roll back to. Assets
# on a public repo are free and do not count against the Actions/Packages storage
# quota, so keeping several costs nothing.
#
# Shared by the release workflow (runs after each publish) and a scheduled workflow
# (enforces the age cap independently of new releases). Requires GH_TOKEN in the env.
set -euo pipefail

REPO="${RELEASES_REPO:-KamilBugaj/ai-usage-app-releases}"
KEEP_NEWEST="${KEEP_NEWEST:-5}"
MAX_AGE_DAYS="${MAX_AGE_DAYS:-60}"
MAX_AGE=$(( MAX_AGE_DAYS * 24 * 3600 ))
NOW=$(date -u +%s)
failures=0

# High --limit ceiling so a backlog can never hide old releases from cleanup;
# retention keeps the active set tiny in practice. Tab-separated to parse safely.
#
# Age comes from publishedAt, not createdAt: GitHub derives a release's createdAt
# from its tag's commit date, so a tag cut well before it ships reads as older than
# it is and loses assets ahead of the cap. Drafts have a null publishedAt, hence the
# createdAt fallback.
releases=$(gh release list --repo "$REPO" --limit 1000 \
  --json tagName,createdAt,publishedAt \
  --jq 'sort_by(.publishedAt // .createdAt) | reverse | to_entries[] | "\(.key)\t\(.value.tagName)\t\(.value.publishedAt // .value.createdAt)"')

while IFS=$'\t' read -r idx tag published; do
  [ -n "$tag" ] || continue
  age=$(( NOW - $(date -u -d "$published" +%s) ))
  age_days=$(( age / 86400 ))

  if [ "$idx" -lt "$KEEP_NEWEST" ]; then
    echo "keep assets (#$(( idx + 1 )) newest, ${age_days}d old): $tag"
    continue
  fi
  if [ "$age" -le "$MAX_AGE" ]; then
    echo "keep assets (within ${MAX_AGE_DAYS}d cap, ${age_days}d old): $tag"
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
