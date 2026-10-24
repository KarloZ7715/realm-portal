#!/usr/bin/env bash
# Assemble a .deb and AppImage from a linux-x64 self-contained dotnet publish directory.
set -euo pipefail

usage() {
  echo "usage: $(basename "$0") <publish-dir> <version> [output-dir]" >&2
  exit 1
}

[[ $# -ge 2 ]] || usage

PUBLISH_DIR="$(readlink -f "$1")"
VERSION="$2"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"${SCRIPT_DIR}/validate-version.sh" "$VERSION"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PACKAGING_DIR="${ROOT_DIR}/packaging/linux"
OUT_DIR="${3:-"${ROOT_DIR}/artifacts/linux"}"
APPIMAGETOOL="${APPIMAGETOOL:-}"

if [[ ! -x "${PUBLISH_DIR}/RealmPortal.App" ]]; then
  echo "error: missing executable ${PUBLISH_DIR}/RealmPortal.App" >&2
  exit 1
fi

mkdir -p "${OUT_DIR}"

install_tree() {
  local dest_lib="$1"
  rm -rf "${dest_lib}"
  mkdir -p "${dest_lib}"
  cp -a "${PUBLISH_DIR}/." "${dest_lib}/"
}

write_deb_launcher() {
  local launcher_path="$1"
  cat >"${launcher_path}" <<'EOF'
#!/bin/sh
set -eu
exec /opt/realm-portal/RealmPortal.App "$@"
EOF
  chmod 755 "${launcher_path}"
}

write_appimage_launcher() {
  local launcher_path="$1"
  cat >"${launcher_path}" <<'EOF'
#!/bin/sh
set -eu
HERE="$(dirname "$(readlink -f "$0")")"
exec "${HERE}/../lib/realm-portal/RealmPortal.App" "$@"
EOF
  chmod 755 "${launcher_path}"
}

# The source metainfo has no version; the <releases> entry is generated from VERSION (Directory.Build.props).
write_metainfo() {
  local dest="$1"
  local release_date
  release_date="$(date -u -d "@${SOURCE_DATE_EPOCH:-$(date +%s)}" +%Y-%m-%d)"
  if grep -q '<releases' "${PACKAGING_DIR}/com.karloz7715.RealmPortal.metainfo.xml"; then
    echo "error: metainfo must not hardcode <releases>; it is generated from the package version" >&2
    exit 1
  fi
  sed "s|^</component>|  <releases>\n    <release version=\"${VERSION}\" date=\"${release_date}\"/>\n  </releases>\n</component>|" \
    "${PACKAGING_DIR}/com.karloz7715.RealmPortal.metainfo.xml" >"${dest}"
  grep -q "<release version=\"${VERSION}\"" "${dest}" || { echo "error: failed to write <releases> into metainfo" >&2; exit 1; }
}

# --- .deb ---
DEB_STAGING="${ROOT_DIR}/pkg-deb/realm-portal_${VERSION}_amd64"
DEB_INSTALL_LIB="/opt/realm-portal"
rm -rf "${DEB_STAGING}"
mkdir -p "${DEB_STAGING}/DEBIAN" \
  "${DEB_STAGING}${DEB_INSTALL_LIB}" \
  "${DEB_STAGING}/usr/bin" \
  "${DEB_STAGING}/usr/share/applications" \
  "${DEB_STAGING}/usr/share/metainfo" \
  "${DEB_STAGING}/usr/share/icons" \
  "${DEB_STAGING}/usr/share/doc/realm-portal"

install_tree "${DEB_STAGING}${DEB_INSTALL_LIB}"
write_deb_launcher "${DEB_STAGING}/usr/bin/realm-portal"
# The self-contained build bundles the .NET runtime: ship its license and notices with ours.
"${ROOT_DIR}/packaging/collect-licenses.sh" "${PUBLISH_DIR}" linux-x64 "${DEB_STAGING}/usr/share/doc/realm-portal"

cp "${PACKAGING_DIR}/com.karloz7715.RealmPortal.desktop" \
  "${DEB_STAGING}/usr/share/applications/com.karloz7715.RealmPortal.desktop"
write_metainfo "${DEB_STAGING}/usr/share/metainfo/com.karloz7715.RealmPortal.metainfo.xml"
cp -a "${PACKAGING_DIR}/icons/hicolor/." "${DEB_STAGING}/usr/share/icons/hicolor/"

cat >"${DEB_STAGING}/DEBIAN/control" <<EOF
Package: realm-portal
Version: ${VERSION}
Section: games
Priority: optional
Architecture: amd64
Maintainer: Realm Portal contributors
Depends: libfontconfig1, libfreetype6, libx11-6, libice6, libsm6
Description: Realm Portal desktop launcher
 Switch between game realms with one click.
EOF

DEB_FILE="${OUT_DIR}/realm-portal_${VERSION}_amd64.deb"
dpkg-deb --build --root-owner-group "${DEB_STAGING}" "${DEB_FILE}"

REQUIRED_DOCS=(
  LICENSE
  THIRD_PARTY_LICENSES.md
  licenses/dotnet-runtime/LICENSE.TXT
  licenses/dotnet-runtime/THIRD-PARTY-NOTICES.TXT
  licenses/nuget/PACKAGES.txt
)
deb_contents="$(dpkg-deb -c "${DEB_FILE}")"
for doc in "${REQUIRED_DOCS[@]}"; do
  grep -q -F "./usr/share/doc/realm-portal/${doc}" <<<"${deb_contents}" || { echo "error: ${doc} missing from ${DEB_FILE}" >&2; exit 1; }
done

# --- AppImage ---
APPDIR="${OUT_DIR}/RealmPortal.AppDir"
APPIMAGE_INSTALL_LIB="${APPDIR}/usr/lib/realm-portal"
rm -rf "${APPDIR}"
mkdir -p "${APPIMAGE_INSTALL_LIB}" \
  "${APPDIR}/usr/bin" \
  "${APPDIR}/usr/share/applications" \
  "${APPDIR}/usr/share/icons"

install_tree "${APPIMAGE_INSTALL_LIB}"
write_appimage_launcher "${APPDIR}/usr/bin/realm-portal"
"${ROOT_DIR}/packaging/collect-licenses.sh" "${PUBLISH_DIR}" linux-x64 "${APPDIR}/usr/share/doc/realm-portal"

cp "${PACKAGING_DIR}/com.karloz7715.RealmPortal.desktop" \
  "${APPDIR}/usr/share/applications/com.karloz7715.RealmPortal.desktop"
cp "${PACKAGING_DIR}/com.karloz7715.RealmPortal.desktop" \
  "${APPDIR}/com.karloz7715.RealmPortal.desktop"
cp -a "${PACKAGING_DIR}/icons/hicolor/." "${APPDIR}/usr/share/icons/hicolor/"
# appimagetool expects a top-level icon named like the Icon= key in the desktop file.
cp "${PACKAGING_DIR}/icons/hicolor/256x256/apps/com.karloz7715.RealmPortal.png" \
  "${APPDIR}/com.karloz7715.RealmPortal.png"

cat >"${APPDIR}/AppRun" <<'EOF'
#!/bin/sh
set -eu
HERE="$(dirname "$(readlink -f "$0")")"
exec "${HERE}/usr/bin/realm-portal" "$@"
EOF
chmod 755 "${APPDIR}/AppRun"
for doc in "${REQUIRED_DOCS[@]}"; do
  [[ -s "${APPDIR}/usr/share/doc/realm-portal/${doc}" ]] || { echo "error: ${doc} missing from the AppDir" >&2; exit 1; }
done

if [[ -z "${APPIMAGETOOL}" ]]; then
  echo "error: set APPIMAGETOOL to the appimagetool AppImage path" >&2
  exit 1
fi
if [[ ! -x "${APPIMAGETOOL}" ]]; then
  echo "error: APPIMAGETOOL is not executable: ${APPIMAGETOOL}" >&2
  exit 1
fi

export ARCH=x86_64
APPIMAGE_FILE="${OUT_DIR}/RealmPortal-${VERSION}-x86_64.AppImage"
# appimagetool runs under FUSE or extracts itself; --appimage-extract-and-run works in CI without user namespaces.
APPIMAGE_EXTRACT_AND_RUN=1 "${APPIMAGETOOL}" --no-appstream "${APPDIR}" "${APPIMAGE_FILE}"

(
  cd "${OUT_DIR}"
  sha256sum "$(basename "${DEB_FILE}")" "$(basename "${APPIMAGE_FILE}")" > SHA256SUMS
)

echo "Wrote ${DEB_FILE}"
echo "Wrote ${APPIMAGE_FILE}"
echo "Wrote ${OUT_DIR}/SHA256SUMS"
