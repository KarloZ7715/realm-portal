#!/usr/bin/env bash
# Self-test for packaging/linux/semver-to-deb-version.sh and prerelease metainfo attrs.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONV="${SCRIPT_DIR}/linux/semver-to-deb-version.sh"
META_SRC="${SCRIPT_DIR}/linux/com.karloz7715.RealmPortal.metainfo.xml"
failures=0

expect_eq() {
  local expected="$1" actual="$2" label="$3"
  if [[ "$actual" == "$expected" ]]; then
    echo "ok: ${label} -> ${actual}"
  else
    echo "FAIL: ${label} -> '${actual}' (expected ${expected})"
    failures=$((failures + 1))
  fi
}

expect_eq "1.0.0" "$("$CONV" 1.0.0)" "stable"
expect_eq "1.0.0~rc.1" "$("$CONV" 1.0.0-rc.1)" "rc.1"
expect_eq "2.3.4~rc.2" "$("$CONV" 2.3.4-rc.2)" "rc.2"
expect_eq "0.0.0~pr.17" "$("$CONV" 0.0.0-pr.17)" "pr.17"

# dpkg must order rc below final
if dpkg --compare-versions "1.0.0~rc.1" lt "1.0.0"; then
  echo "ok: dpkg 1.0.0~rc.1 < 1.0.0"
else
  echo "FAIL: dpkg ordering for ~rc"
  failures=$((failures + 1))
fi
if dpkg --compare-versions "1.0.0-rc.1" gt "1.0.0"; then
  echo "ok: dpkg 1.0.0-rc.1 > 1.0.0 (why we need ~)"
else
  echo "FAIL: unexpected dpkg ordering for dash rc"
  failures=$((failures + 1))
fi

# Metainfo generation smoke (mirrors build-packages.sh write_metainfo)
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
SOURCE_DATE_EPOCH=1700000000
release_date="$(date -u -d "@${SOURCE_DATE_EPOCH}" +%Y-%m-%d)"
VERSION=1.1.0-rc.1
IS_PRERELEASE=1
release_attrs="version=\"${VERSION}\" date=\"${release_date}\" type=\"development\""
sed "s|^</component>|  <releases>\n    <release ${release_attrs}/>\n  </releases>\n</component>|" \
  "$META_SRC" >"${tmp}/meta.xml"
grep -q 'version="1.1.0-rc.1"' "${tmp}/meta.xml" && grep -q 'type="development"' "${tmp}/meta.xml" \
  && echo "ok: prerelease metainfo attrs" \
  || { echo "FAIL: prerelease metainfo"; failures=$((failures + 1)); }

VERSION=1.1.0
IS_PRERELEASE=0
release_attrs="version=\"${VERSION}\" date=\"${release_date}\""
sed "s|^</component>|  <releases>\n    <release ${release_attrs}/>\n  </releases>\n</component>|" \
  "$META_SRC" >"${tmp}/meta-stable.xml"
grep -q 'version="1.1.0"' "${tmp}/meta-stable.xml" \
  && ! grep -q 'type="development"' "${tmp}/meta-stable.xml" \
  && echo "ok: stable metainfo has no development type" \
  || { echo "FAIL: stable metainfo"; failures=$((failures + 1)); }

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all deb-version checks passed"
