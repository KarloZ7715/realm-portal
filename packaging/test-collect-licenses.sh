#!/usr/bin/env bash
# Self-test for packaging/collect-licenses.sh with a synthetic publish folder and runtime pack (no real publish needed).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COLLECT="${SCRIPT_DIR}/collect-licenses.sh"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

pack="${tmp}/nuget/microsoft.netcore.app.runtime.linux-x64/9.0.99"
mkdir -p "$pack" "${tmp}/publish"
printf 'The MIT License (MIT)\n' >"${pack}/LICENSE.TXT"
printf 'notices\n' >"${pack}/THIRD-PARTY-NOTICES.TXT"

MANIFEST="${SCRIPT_DIR}/../licenses/nuget/packages.tsv"
# Every package of the real manifest, as deps.json "libraries" entries (keeps the required-package rule satisfied).
known_entries="$(tr -d '\r' <"$MANIFEST" | awk -F'\t' '!/^#/ && NF >= 4 { printf ",\n    \"%s/%s\": {\n      \"type\": \"package\",\n      \"serviceable\": true\n    }", $1, $2 }')"

write_deps() {
  local extra="$1"
  cat >"${tmp}/publish/RealmPortal.App.deps.json" <<JSON
{
  "targets": { ".NETCoreApp,Version=v9.0/linux-x64": {} },
  "libraries": {
    "RealmPortal.App/1.0.0": {
      "type": "project",
      "serviceable": false
    },
    "runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/9.0.99": {
      "type": "runtimepack",
      "serviceable": false
    },
    "Avalonia.BuildServices/11.3.2": {
      "type": "package",
      "serviceable": true
    }${known_entries}${extra}
  }
}
JSON
}

failures=0
run() { NUGET_PACKAGES="${tmp}/nuget" "$COLLECT" "${tmp}/publish" linux-x64 "${tmp}/out" >"${tmp}/log" 2>&1; }

write_deps ""
if run && [[ -s "${tmp}/out/licenses/nuget/avalonia-11.3.22/licence.md" ]] && grep -q '^Avalonia	11.3.22	MIT' "${tmp}/out/licenses/nuget/PACKAGES.txt"; then
  echo "ok: known package is collected"
else
  echo "FAIL: known package"; cat "${tmp}/log"; failures=$((failures + 1))
fi

write_deps ',
    "Some.Unlisted.Package/1.2.3": {
      "type": "package",
      "serviceable": true
    }'
if run; then
  echo "FAIL: unlisted package was accepted"; failures=$((failures + 1))
elif grep -q 'Some.Unlisted.Package/1.2.3' "${tmp}/log"; then
  echo "ok: unlisted package fails the build"
else
  echo "FAIL: unlisted package error does not name it"; cat "${tmp}/log"; failures=$((failures + 1))
fi

write_deps ',
    "Avalonia.X11/99.0.0": {
      "type": "package",
      "serviceable": true
    }'
if run; then
  echo "FAIL: package with an unlisted version was accepted"; failures=$((failures + 1))
else
  echo "ok: version mismatch fails the build"
fi

# A required package (PeNet) missing from the publish fails the build.
saved="$known_entries"
known_entries="$(printf '%s' "$saved" | awk 'BEGIN { RS = ",\n    "; ORS = "" } !/"PeNet\/5\.1\.0"/ { if (NR > 1 && $0 != "") printf ",\n    %s", $0 }')"
write_deps ""
if run; then
  echo "FAIL: missing required package was accepted"; failures=$((failures + 1))
elif grep -q 'required package PeNet ' "${tmp}/log"; then
  echo "ok: missing required package fails the build"
else
  echo "FAIL: missing required package error"; cat "${tmp}/log"; failures=$((failures + 1))
fi
known_entries="$saved"

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all collect-licenses checks passed"
