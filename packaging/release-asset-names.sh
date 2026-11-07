#!/usr/bin/env bash
# Print the release binary asset names for a SemVer package version, one per line:
# the .deb, the AppImage and the Windows zip (SHA256SUMS lists exactly these).
# Every file name uses the SemVer version as is (e.g. 1.2.0-rc.1). The Debian control
# Version field still uses "~" (see linux/semver-to-deb-version.sh), but a file name must
# not: GitHub renames "~" to "." in uploaded asset names, so the uploaded name, SHA256SUMS
# and the in-app updater would disagree.
set -euo pipefail

[[ $# -eq 1 && -n "$1" ]] || { echo "usage: $(basename "$0") <semver-version>" >&2; exit 1; }
v="$1"
names=(
  "realm-portal_${v}_amd64.deb"
  "RealmPortal-${v}-x86_64.AppImage"
  "RealmPortal-${v}-win-x64.zip"
)
for n in "${names[@]}"; do
  # Only characters GitHub keeps unchanged in asset names.
  [[ "$n" =~ ^[A-Za-z0-9._+-]+$ ]] || { echo "error: unsafe release asset name: ${n}" >&2; exit 1; }
  printf '%s\n' "$n"
done
