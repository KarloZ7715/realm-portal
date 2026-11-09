#!/usr/bin/env bash
# Self-test for packaging/check-install-kind.sh and the build-packages.sh argument checks (#206).
# Uses stub binaries in a temp dir; no dotnet, dpkg-deb or appimagetool needed.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHECK="${SCRIPT_DIR}/check-install-kind.sh"
BUILD="${SCRIPT_DIR}/linux/build-packages.sh"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
failures=0

fail() {
  echo "FAIL: $1"
  failures=$((failures + 1))
}

stub() {
  local dir="$1" line="$2"
  mkdir -p "$dir"
  # shellcheck disable=SC2016 # $1 belongs to the stub, not to this script
  printf '#!/bin/sh\n[ "$1" = --print-install-kind ] || exit 9\necho "%s"\n' "$line" >"$dir/RealmPortal.App"
  chmod +x "$dir/RealmPortal.App"
}

stub "$tmp/deb" "Deb self-replace=false"
stub "$tmp/appimage" "AppImage self-replace=true"
stub "$tmp/unknown" "Unknown self-replace=false"
stub "$tmp/noreplace" "AppImage self-replace=false"

"$CHECK" "$tmp/deb/RealmPortal.App" Deb false >/dev/null || fail "matching Deb was rejected"
"$CHECK" "$tmp/appimage/RealmPortal.App" AppImage true >/dev/null || fail "matching AppImage was rejected"
if "$CHECK" "$tmp/unknown/RealmPortal.App" Deb false 2>/dev/null; then fail "Unknown accepted as Deb"; fi
if "$CHECK" "$tmp/deb/RealmPortal.App" AppImage true 2>/dev/null; then fail "Deb accepted as AppImage"; fi
if "$CHECK" "$tmp/noreplace/RealmPortal.App" AppImage true 2>/dev/null; then fail "self-replace=false accepted"; fi
if "$CHECK" "$tmp/missing" Deb false 2>/dev/null; then fail "missing binary accepted"; fi
if "$CHECK" "$tmp/deb/RealmPortal.App" Deb 2>/dev/null; then fail "missing argument accepted"; fi

# build-packages.sh: the old single-directory form and a shared directory must fail before packaging anything.
if "$BUILD" "$tmp/deb" 1.2.0 "$tmp/out" 2>/dev/null; then fail "build-packages.sh accepted one publish dir"; fi
if "$BUILD" "$tmp/deb" "$tmp/deb" 1.2.0 "$tmp/out" 2>/dev/null; then fail "build-packages.sh accepted the same dir twice"; fi
if "$BUILD" "$tmp/deb" "$tmp/missing" 1.2.0 "$tmp/out" 2>/dev/null; then fail "build-packages.sh accepted a missing AppImage dir"; fi
[[ ! -e "$tmp/out" ]] || fail "build-packages.sh wrote output after an argument error"

if [[ $failures -ne 0 ]]; then
  echo "$failures failure(s)"
  exit 1
fi
echo "check-install-kind and build-packages argument tests passed"
