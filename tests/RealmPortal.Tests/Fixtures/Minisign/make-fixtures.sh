#!/usr/bin/env bash
# Regenerates the TEST-ONLY minisign fixtures in this folder (see README.md). Needs the minisign CLI (0.11 or later).
# The key pairs are throwaway: they are created in a temp dir, used once, and the secret keys are deleted. They have
# nothing to do with the real release key.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

for k in a b unknown; do
  minisign -G -W -f -p "$tmp/$k.pub" -s "$tmp/$k.key" >/dev/null
  cp "$tmp/$k.pub" "$here/test-key-$k.pub"
done

# Synthetic SHA256SUMS: the digests are of placeholder strings, not of real packages.
sums() {
  local v="$1" name
  for name in "realm-portal_${v}_amd64.deb" "RealmPortal-${v}-x86_64.AppImage" "RealmPortal-${v}-win-x64.zip"; do
    printf '%s  %s\n' "$(printf 'test fixture %s' "$name" | sha256sum | cut -d' ' -f1)" "$name"
  done
}
sums 1.2.0 > "$here/SHA256SUMS"
sums 1.1.9 > "$here/SHA256SUMS-1.1.9"

sign() { # key file sig trusted-comment [extra minisign args]
  local key="$1" file="$2" sig="$3" tc="$4"
  shift 4
  minisign -S "$@" -s "$tmp/$key.key" -m "$here/$file" -x "$here/$sig" -t "$tc" -c "test-only signature" >/dev/null
}
sign a SHA256SUMS SHA256SUMS.a.minisig "realm-portal v1.2.0"
sign b SHA256SUMS SHA256SUMS.b.minisig "realm-portal v1.2.0"
sign unknown SHA256SUMS SHA256SUMS.unknown.minisig "realm-portal v1.2.0"
sign a SHA256SUMS SHA256SUMS.a-rc.minisig "realm-portal v1.2.0-rc.1"
sign a SHA256SUMS SHA256SUMS.a-legacy.minisig "realm-portal v1.2.0" -l
sign a SHA256SUMS-1.1.9 SHA256SUMS-1.1.9.a.minisig "realm-portal v1.1.9"

# Cross-check with the reference implementation. -H refuses legacy signatures, which is what the app does too.
for s in a b unknown a-rc; do
  minisign -V -H -q -p "$here/test-key-${s%-rc}.pub" -m "$here/SHA256SUMS" -x "$here/SHA256SUMS.$s.minisig"
done
minisign -V -H -q -p "$here/test-key-a.pub" -m "$here/SHA256SUMS-1.1.9" -x "$here/SHA256SUMS-1.1.9.a.minisig"
minisign -V -q -p "$here/test-key-a.pub" -m "$here/SHA256SUMS" -x "$here/SHA256SUMS.a-legacy.minisig"
if minisign -V -H -q -p "$here/test-key-a.pub" -m "$here/SHA256SUMS" -x "$here/SHA256SUMS.a-legacy.minisig" 2>/dev/null; then
  echo "error: minisign -H accepted the legacy fixture" >&2
  exit 1
fi
echo "Fixtures written and verified with $(minisign -v)"
