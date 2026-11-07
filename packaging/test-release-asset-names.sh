#!/usr/bin/env bash
# Self-test for packaging/release-asset-names.sh: release asset names must never contain "~"
# (GitHub renames it to "."), while the Debian Version field keeps "~" for prereleases.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NAMES="${RELEASE_ASSET_NAMES:-${SCRIPT_DIR}/release-asset-names.sh}"
CONV="${SCRIPT_DIR}/linux/semver-to-deb-version.sh"
failures=0

fail() {
  echo "FAIL: $1"
  failures=$((failures + 1))
}

for ver in 1.2.0 1.2.0-rc.1 2.3.4-rc.2 0.0.0-pr.17; do
  if ! out="$("$NAMES" "$ver")"; then
    fail "${ver}: asset name script failed"
    continue
  fi
  mapfile -t names <<<"$out"
  [[ ${#names[@]} -eq 3 ]] || fail "${ver}: expected 3 names, got ${#names[@]}"
  for n in "${names[@]}"; do
    if [[ "$n" == *"~"* ]]; then
      fail "${ver}: release asset name contains '~': ${n}"
    elif [[ "$n" != *"${ver}"* ]]; then
      fail "${ver}: release asset name lacks the SemVer version: ${n}"
    else
      echo "ok: ${ver} -> ${n}"
    fi
  done
done

# Pin the exact names (and order) for a final and an rc version, so any drift in the helper fails.
expect_names() { # <version> <deb> <appimage> <zip>
  local ver="$1" got want
  shift
  got="$("$NAMES" "$ver" 2>/dev/null || true)"
  want="$(printf '%s\n' "$@")"
  if [[ "$got" == "$want" ]]; then
    echo "ok: ${ver} exact names"
  else
    fail "${ver}: names differ. got=(${got//$'\n'/ }) want=(${want//$'\n'/ })"
  fi
}
expect_names 1.2.0 realm-portal_1.2.0_amd64.deb RealmPortal-1.2.0-x86_64.AppImage RealmPortal-1.2.0-win-x64.zip
expect_names 1.2.0-rc.1 realm-portal_1.2.0-rc.1_amd64.deb RealmPortal-1.2.0-rc.1-x86_64.AppImage \
  RealmPortal-1.2.0-rc.1-win-x64.zip

# The Debian Version field (not the file name) keeps "~" so dpkg orders the rc first.
if [[ "$("$CONV" 1.2.0-rc.1)" == "1.2.0~rc.1" ]]; then
  echo "ok: Debian Version field keeps ~rc.1"
else
  fail "Debian Version field for 1.2.0-rc.1"
fi

# The script itself refuses a name GitHub would rename.
if "${SCRIPT_DIR}/release-asset-names.sh" '1.2.0~rc.1' >/dev/null 2>&1; then
  fail "release-asset-names.sh accepted a '~' version"
else
  echo "ok: '~' version rejected"
fi

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all release asset name checks passed"
