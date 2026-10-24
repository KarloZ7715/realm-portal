#!/usr/bin/env bash
# Copy the license and notice files that must ship with a self-contained release build into <dest-dir>.
#
# Layout written to <dest-dir>:
#   LICENSE                                  Realm Portal (MIT)
#   THIRD_PARTY_LICENSES.md                  inventory of third-party software and assets
#   licenses/dotnet-runtime/LICENSE.TXT      .NET runtime license (MIT)
#   licenses/dotnet-runtime/THIRD-PARTY-NOTICES.TXT
#   licenses/dotnet-runtime/VERSION.txt      runtime pack version and source commit
#   licenses/fonts/OFL-<family>.txt          SIL OFL 1.1 of each bundled typeface
#
# The .NET runtime files come from the exact runtime pack that `dotnet publish --self-contained` bundled: its version is
# read from <publish-dir>/RealmPortal.App.deps.json and the pack is taken from the NuGet global packages folder.
set -euo pipefail

usage() {
  echo "usage: $(basename "$0") <publish-dir> <rid> <dest-dir>" >&2
  exit 1
}

fail() {
  echo "error: $*" >&2
  exit 1
}

[[ $# -eq 3 ]] || usage
PUBLISH_DIR="$1"
RID="$2"
DEST_DIR="$3"
if command -v cygpath >/dev/null 2>&1; then
  # Git Bash on Windows runners: accept C:\... paths from pwsh steps.
  PUBLISH_DIR="$(cygpath -u "$PUBLISH_DIR")"
  DEST_DIR="$(cygpath -u "$DEST_DIR")"
fi
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

[[ "$RID" =~ ^[a-z0-9-]+$ ]] || fail "invalid RID: ${RID}"
deps="${PUBLISH_DIR}/RealmPortal.App.deps.json"
[[ -f "$deps" ]] || fail "missing ${deps} (expected a self-contained publish)"

runtime_version="$(grep -o -E "\"runtimepack\.Microsoft\.NETCore\.App\.Runtime\.${RID}/[^\"]+\"" "$deps" | head -n 1 | sed -E 's/.*\/([^"]+)"/\1/' || true)"
[[ -n "$runtime_version" ]] || fail "no Microsoft.NETCore.App runtime pack for ${RID} in ${deps} (not a self-contained publish?)"

nuget_root="${NUGET_PACKAGES:-}"
if [[ -z "$nuget_root" ]] && command -v dotnet >/dev/null 2>&1; then
  nuget_root="$(dotnet nuget locals global-packages --list | sed -E -n 's/^global-packages:[[:space:]]*//p' | tr -d '\r' | head -n 1)"
fi
nuget_root="${nuget_root:-"${HOME}/.nuget/packages"}"
if command -v cygpath >/dev/null 2>&1; then
  nuget_root="$(cygpath -u "$nuget_root")"
fi
pack_dir="${nuget_root%/}/microsoft.netcore.app.runtime.${RID}/${runtime_version,,}"
[[ -d "$pack_dir" ]] || fail "runtime pack not found: ${pack_dir}"

copy_file() {
  local src="$1" dest="$2"
  [[ -s "$src" ]] || fail "missing or empty ${src}"
  mkdir -p "$(dirname "$dest")"
  cp "$src" "$dest"
  chmod 644 "$dest"
}

copy_file "${ROOT_DIR}/LICENSE" "${DEST_DIR}/LICENSE"
copy_file "${ROOT_DIR}/THIRD_PARTY_LICENSES.md" "${DEST_DIR}/THIRD_PARTY_LICENSES.md"
copy_file "${pack_dir}/LICENSE.TXT" "${DEST_DIR}/licenses/dotnet-runtime/LICENSE.TXT"
copy_file "${pack_dir}/THIRD-PARTY-NOTICES.TXT" "${DEST_DIR}/licenses/dotnet-runtime/THIRD-PARTY-NOTICES.TXT"

commit=""
if [[ -f "${pack_dir}/Microsoft.NETCore.App.versions.txt" ]]; then
  commit="$(head -n 1 "${pack_dir}/Microsoft.NETCore.App.versions.txt" | tr -d '\r')"
fi
{
  echo "Microsoft.NETCore.App runtime ${runtime_version} (${RID}), bundled by dotnet publish --self-contained."
  echo "Source: https://github.com/dotnet/runtime${commit:+/tree/${commit}}"
  echo "License: MIT, Copyright (c) .NET Foundation and Contributors (see LICENSE.TXT and THIRD-PARTY-NOTICES.TXT)."
} >"${DEST_DIR}/licenses/dotnet-runtime/VERSION.txt"
chmod 644 "${DEST_DIR}/licenses/dotnet-runtime/VERSION.txt"

declare -A fonts=(
  [Almendra]=almendra
  [MarcellusSC]=marcellussc
  [Alegreya]=alegreya
  [AlegreyaSans]=alegreyasans
  [JetBrainsMono]=jetbrainsmono
)
for name in "${!fonts[@]}"; do
  copy_file "${ROOT_DIR}/assets/fonts/${fonts[$name]}/OFL.txt" "${DEST_DIR}/licenses/fonts/OFL-${name}.txt"
done

grep -q 'MIT' "${DEST_DIR}/licenses/dotnet-runtime/LICENSE.TXT" || fail "unexpected .NET runtime LICENSE.TXT"
echo "Wrote license files for .NET runtime ${runtime_version} (${RID}) to ${DEST_DIR}"
