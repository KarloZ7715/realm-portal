#!/usr/bin/env bash
# Resolve the package version for a release workflow run.
#
# Directory.Build.props <Version> is the single source of the product version. This script reads it and:
#   - pull_request:      0.0.0-pr.<PR number>  (test artifacts only; sorts below every real release in dpkg, so an
#                        installed PR .deb never blocks the upgrade to a release)
#   - push (tag vX.Y.Z or vX.Y.Z-<prerelease>): the tag without "v"; it MUST be <props> or <props>-<suffix>
#   - workflow_dispatch: <props>, or the "version" input if it is <props> or a prerelease of it (<props>-<suffix>)
#
# Inputs come from the environment: EVENT_NAME, REF_NAME, INPUT_VERSION, PR_NUMBER, and optionally
# PROPS_FILE (defaults to Directory.Build.props at the repository root). Prints the version on stdout.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROPS_FILE="${PROPS_FILE:-"${ROOT_DIR}/Directory.Build.props"}"
VERSION_REGEX='^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$'

fail() {
  echo "error: $*" >&2
  exit 1
}

[[ -f "$PROPS_FILE" ]] || fail "missing ${PROPS_FILE}"
props_version="$(sed -n -E 's/.*<Version>[[:space:]]*([^<[:space:]]+)[[:space:]]*<\/Version>.*/\1/p' "$PROPS_FILE" | head -n 1 | tr -d '\r')"
[[ -n "$props_version" ]] || fail "no <Version> in ${PROPS_FILE}"
[[ "$props_version" =~ $VERSION_REGEX ]] || fail "invalid <Version> in ${PROPS_FILE}: ${props_version}"

case "${EVENT_NAME:-}" in
  pull_request)
    [[ "${PR_NUMBER:-}" =~ ^[0-9]+$ ]] || fail "missing or invalid pull request number"
    version="0.0.0-pr.${PR_NUMBER}"
    ;;
  push)
    tag="${REF_NAME:-}"
    [[ "$tag" == v* ]] || fail "push runs must come from a v<version> tag (got '${tag}')"
    version="${tag#v}"
    if [[ "$version" != "$props_version" && "$version" != "${props_version}-"* ]]; then
      fail "tag ${tag} must be v${props_version} or v${props_version}-<prerelease> (Directory.Build.props); bump Directory.Build.props first"
    fi
    ;;
  workflow_dispatch)
    version="${INPUT_VERSION:-}"
    if [[ -z "$version" ]]; then
      version="$props_version"
    elif [[ "$version" != "$props_version" && "$version" != "${props_version}-"* ]]; then
      fail "input version ${version} must be ${props_version} or ${props_version}-<prerelease> (Directory.Build.props)"
    fi
    ;;
  *)
    fail "unsupported event '${EVENT_NAME:-}'"
    ;;
esac

[[ "$version" =~ $VERSION_REGEX ]] || fail "invalid package version (expected ${VERSION_REGEX}): ${version}"
echo "$version"
