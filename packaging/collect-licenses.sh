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
#   licenses/nuget/<folder>/                 license and notice files of every NuGet package in the publish
#   licenses/nuget/PACKAGES.txt              shipped packages: id, version, license, folder
#
# The .NET runtime files come from the exact runtime pack that `dotnet publish --self-contained` bundled: its version is
# read from <publish-dir>/RealmPortal.App.deps.json and the pack is taken from the NuGet global packages folder.
#
# NuGet packages: every "package" library in that deps.json must have an exact id+version row in the committed
# licenses/nuget/packages.tsv; every file the row lists must exist in its folder and be non-empty, and the folder must not hold
# unlisted files; otherwise the script fails. Only our own projects
# (RealmPortal.*) and the Microsoft.NETCore.App runtime pack (covered above) are allowed without a row.
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

# --- NuGet packages ---
MANIFEST="${ROOT_DIR}/licenses/nuget/packages.tsv"
[[ -f "$MANIFEST" ]] || fail "missing ${MANIFEST}"
declare -A row_license=() row_folder=() folder_files=()
while IFS=$'\t' read -r id ver license folder files _source; do
  [[ -z "$id" || "$id" == \#* ]] && continue
  [[ -n "$ver" && -n "$license" && -n "$folder" && -n "$files" && -n "$_source" ]] || fail "malformed row in ${MANIFEST}: ${id}"
  [[ "$folder" =~ ^[A-Za-z0-9._-]+$ ]] || fail "invalid folder in ${MANIFEST}: ${folder}"
  [[ "$files" =~ ^[A-Za-z0-9._-]+(,[A-Za-z0-9._-]+)*$ ]] || fail "invalid file list in ${MANIFEST} for ${id}: ${files}"
  if [[ -n "${folder_files[$folder]:-}" && "${folder_files[$folder]}" != "$files" ]]; then
    fail "rows for licenses/nuget/${folder} list different files (${folder_files[$folder]} vs ${files})"
  fi
  folder_files["$folder"]="$files"
  row_license["${id}/${ver}"]="$license"
  row_folder["${id}/${ver}"]="$folder"
done < <(tr -d '\r' <"$MANIFEST")

# Print the problems of a license folder against its expected file list (nothing when it is complete).
folder_problems() {
  local folder="$1" dir="${ROOT_DIR}/licenses/nuget/$1" f
  local -a expected=()
  IFS=',' read -r -a expected <<<"${folder_files[$folder]}"
  if [[ ! -d "$dir" ]]; then
    echo "folder licenses/nuget/${folder} missing"
    return
  fi
  for f in "${expected[@]}"; do
    [[ -s "${dir}/${f}" ]] || echo "licenses/nuget/${folder}/${f} missing or empty"
  done
  # find (not a glob) so hidden files and subfolders are reported too.
  while IFS= read -r f; do
    [[ ",${folder_files[$folder]}," == *",$(basename "$f"),"* ]] || echo "licenses/nuget/${folder}/$(basename "$f") is not listed in packages.tsv"
  done < <(find "$dir" -mindepth 1 -maxdepth 1 | LC_ALL=C sort)
  return 0
}

# List "<type>\t<id>/<version>" for each entry of the deps.json "libraries" object.
libraries="$(tr -d '\r' <"$deps" | awk '
  /"libraries"[[:space:]]*:/ { inlib = 1; next }
  inlib && match($0, /^[[:space:]]*"[^"]+\/[^"]+"[[:space:]]*:[[:space:]]*\{/) {
    key = $0; sub(/^[[:space:]]*"/, "", key); sub(/"[[:space:]]*:.*/, "", key); next
  }
  inlib && key != "" && match($0, /"type"[[:space:]]*:[[:space:]]*"[^"]+"/) {
    t = substr($0, RSTART, RLENGTH); sub(/.*:[[:space:]]*"/, "", t); sub(/"$/, "", t)
    print t "\t" key; key = ""
  }')"
[[ -n "$libraries" ]] || fail "no libraries found in ${deps}"

missing=()
declare -A folders=()
packages_list=""
package_count=0
while IFS=$'\t' read -r type key; do
  case "$type" in
    project)
      [[ "$key" == RealmPortal.* ]] || missing+=("${key} (unexpected project)")
      ;;
    runtimepack)
      [[ "$key" == "runtimepack.Microsoft.NETCore.App.Runtime.${RID}/${runtime_version}" ]] || missing+=("${key} (unexpected runtime pack)")
      ;;
    package)
      folder="${row_folder[$key]:-}"
      problems=""
      [[ -z "$folder" ]] || problems="$(folder_problems "$folder")"
      if [[ -z "$folder" ]]; then
        missing+=("${key} (no row in licenses/nuget/packages.tsv)")
      elif [[ -n "$problems" ]]; then
        while IFS= read -r problem; do missing+=("${key} (${problem})"); done <<<"$problems"
      else
        folders["$folder"]=1
        packages_list+="${key%/*}"$'\t'"${key##*/}"$'\t'"${row_license[$key]}"$'\t'"${folder}"$'\n'
        package_count=$((package_count + 1))
      fi
      ;;
    *)
      missing+=("${key} (unknown library type ${type})")
      ;;
  esac
done <<<"$libraries"

if [[ ${#missing[@]} -gt 0 ]]; then
  printf 'error: published dependency without license files: %s\n' "${missing[@]}" >&2
  echo "Add the package's license/notice files under licenses/nuget/<folder>/ and a row to licenses/nuget/packages.tsv." >&2
  exit 1
fi

# Packages whose license texts must always ship (release QA): fail if one is not in the published, licensed set.
REQUIRED_PACKAGES=(
  Avalonia
  Avalonia.Angle.Windows.Natives
  CommunityToolkit.Mvvm
  HarfBuzzSharp
  MicroCom.Runtime
  PeNet
  PeNet.Asn1
  SkiaSharp
  Tmds.DBus.Protocol
)
for id in "${REQUIRED_PACKAGES[@]}"; do
  awk -F'\t' -v id="$id" '$1 == id { found = 1 } END { exit !found }' <<<"$packages_list" || fail "required package ${id} is not in ${deps} with license files"
done

mkdir -p "${DEST_DIR}/licenses/nuget"
for folder in "${!folders[@]}"; do
  IFS=',' read -r -a listed <<<"${folder_files[$folder]}"
  for f in "${listed[@]}"; do
    copy_file "${ROOT_DIR}/licenses/nuget/${folder}/${f}" "${DEST_DIR}/licenses/nuget/${folder}/${f}"
  done
done
{
  echo "NuGet packages bundled in this ${RID} build (id, version, license, folder under licenses/nuget/):"
  printf '%s' "$packages_list" | sort
} >"${DEST_DIR}/licenses/nuget/PACKAGES.txt"
chmod 644 "${DEST_DIR}/licenses/nuget/PACKAGES.txt"

grep -q 'MIT' "${DEST_DIR}/licenses/dotnet-runtime/LICENSE.TXT" || fail "unexpected .NET runtime LICENSE.TXT"
echo "Wrote license files for ${package_count} NuGet packages (${#folders[@]} folders) to ${DEST_DIR}/licenses/nuget"
echo "Wrote license files for .NET runtime ${runtime_version} (${RID}) to ${DEST_DIR}"
