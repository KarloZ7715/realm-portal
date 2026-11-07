#!/usr/bin/env bash
# Map a SemVer package version to the Debian control Version field (not the .deb file name,
# which keeps SemVer: GitHub renames "~" in release asset names).
# "1.0.0-rc.1" must become "1.0.0~rc.1" so dpkg sorts it below the final "1.0.0".
set -euo pipefail

usage() {
  echo "usage: $(basename "$0") <semver-version>" >&2
  exit 1
}

[[ $# -eq 1 ]] || usage
v="$1"
if [[ "$v" == *-* ]]; then
  printf '%s~%s\n' "${v%%-*}" "${v#*-}"
else
  printf '%s\n' "$v"
fi
