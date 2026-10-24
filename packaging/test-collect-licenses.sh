#!/usr/bin/env bash
# Self-test for packaging/collect-licenses.sh with a synthetic publish folder and runtime pack (no real publish needed).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Work on a copy of the inputs the script reads, so cases can delete license files without touching the checkout.
repo="${tmp}/repo"
mkdir -p "${repo}/assets"
cp -R "${REPO_DIR}/packaging" "${REPO_DIR}/licenses" "${repo}/"
cp -R "${REPO_DIR}/assets/fonts" "${repo}/assets/"
cp "${REPO_DIR}/LICENSE" "${REPO_DIR}/THIRD_PARTY_LICENSES.md" "${repo}/"
COLLECT="${repo}/packaging/collect-licenses.sh"

pack="${tmp}/nuget/microsoft.netcore.app.runtime.linux-x64/9.0.99"
mkdir -p "$pack" "${tmp}/publish"
printf 'The MIT License (MIT)\n' >"${pack}/LICENSE.TXT"
printf 'notices\n' >"${pack}/THIRD-PARTY-NOTICES.TXT"

MANIFEST="${repo}/licenses/nuget/packages.tsv"
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

# Every file listed for a folder is required: removing or emptying a single one fails, and so does an unlisted extra file.
expect_file_failure() {
  local label="$1" pattern="$2"
  write_deps ""
  if run; then
    echo "FAIL: ${label} was accepted"; failures=$((failures + 1))
  elif grep -q -F -- "$pattern" "${tmp}/log"; then
    echo "ok: ${label} fails the build"
  else
    echo "FAIL: ${label} error does not name the file"; cat "${tmp}/log"; failures=$((failures + 1))
  fi
}

nuget="${repo}/licenses/nuget"
mv "${nuget}/penet.asn1-2.0.1/LICENSE-Apache-2.0.txt" "${tmp}/saved"
expect_file_failure "deleting penet.asn1-2.0.1/LICENSE-Apache-2.0.txt" "penet.asn1-2.0.1/LICENSE-Apache-2.0.txt missing or empty"
mv "${tmp}/saved" "${nuget}/penet.asn1-2.0.1/LICENSE-Apache-2.0.txt"

mv "${nuget}/skiasharp-2.88.9/THIRD-PARTY-NOTICES.txt" "${tmp}/saved"
expect_file_failure "deleting skiasharp-2.88.9/THIRD-PARTY-NOTICES.txt" "skiasharp-2.88.9/THIRD-PARTY-NOTICES.txt missing or empty"
mv "${tmp}/saved" "${nuget}/skiasharp-2.88.9/THIRD-PARTY-NOTICES.txt"

cp "${nuget}/avalonia-11.3.22/NOTICE.md" "${tmp}/saved"
: >"${nuget}/avalonia-11.3.22/NOTICE.md"
expect_file_failure "emptying avalonia-11.3.22/NOTICE.md" "avalonia-11.3.22/NOTICE.md missing or empty"
mv "${tmp}/saved" "${nuget}/avalonia-11.3.22/NOTICE.md"

printf 'x\n' >"${nuget}/penet-5.1.0/EXTRA.txt"
expect_file_failure "an unlisted file in penet-5.1.0" "penet-5.1.0/EXTRA.txt is not listed"
rm "${nuget}/penet-5.1.0/EXTRA.txt"

printf 'x\n' >"${nuget}/penet-5.1.0/.hidden"
expect_file_failure "a hidden unlisted file in penet-5.1.0" "penet-5.1.0/.hidden is not listed"
rm "${nuget}/penet-5.1.0/.hidden"

write_deps ""
if run; then
  echo "ok: restored license files are accepted again"
else
  echo "FAIL: restored license files"; cat "${tmp}/log"; failures=$((failures + 1))
fi

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all collect-licenses checks passed"
