#!/usr/bin/env bash
# Validate a package version string (semver core + optional prerelease suffix).
set -euo pipefail

VERSION_REGEX='^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$'

usage() {
  echo "usage: $(basename "$0") <version>" >&2
  exit 1
}

[[ $# -eq 1 ]] || usage

version="$1"
if [[ ! "$version" =~ $VERSION_REGEX ]]; then
  echo "error: invalid package version (expected ${VERSION_REGEX}): ${version}" >&2
  exit 1
fi

echo "ok: ${version}"
