# Release packaging notes (F6)

## CI workflow

The [Release (Linux and Windows packages)](https://github.com/KarloZ7715/realm-portal/actions/workflows/release-linux.yml) workflow (`.github/workflows/release-linux.yml`) builds Linux packages and a Windows archive:

The version has a single source: `<Version>` in `Directory.Build.props`. `packaging/resolve-version.sh` reads it (self-tested by
`packaging/test-resolve-version.sh` in the Linux job) and both jobs pass the result to `dotnet publish` as `-p:Version` /
`-p:InformationalVersion`, so the generated AppStream `<releases>` entry, the status bar and `realm-portal --version`
use the product version. The Debian package filename and `.deb` `Version:` field use the Debian-normalized form, where
prerelease `-` becomes `~`.

| Trigger | Version used |
| --- | --- |
| `pull_request` (path-filtered) | `0.0.0-pr.<PR number>` (artifacts only; no release; the props version is still read and validated). It stays below every real release so an installed PR `.deb` never blocks an upgrade |
| `workflow_dispatch` | `<props version>` when the `version` input is empty; otherwise the input, which must be `<props version>` or a prerelease of it (`<props version>-rc.1`) |
| Push tag matching `v[0-9]+.[0-9]+.[0-9]+*` | Tag without the `v` prefix; it **must be** `<props version>` or `<props version>-<prerelease>` (e.g. `v1.1.0` or `v1.1.0-rc.1` with `<Version>1.1.0</Version>`), otherwise the run fails before building |

To release: bump `<Version>` in `Directory.Build.props` in a PR, merge it, then push the matching `v<version>` tag (or `v<version>-rc.N` for a prerelease).

On a tag push, after both package jobs succeed, the **publish** job in `.github/workflows/release-linux.yml` creates a **draft** GitHub Release, uploads every package plus a merged `SHA256SUMS`, verifies digests/sizes/asset names (draft download URLs use `untagged-*` until publish), publishes, then asserts the final `/releases/download/<tag>/…` URL shape. An `rc` tag is published with `--prerelease --latest=false` so GitHub’s `/releases/latest` (and the in-app update check) never offers it. Stable tags become the latest release. The job refuses to modify an already-published release; if the published release is not immutable, it is reverted to draft and the job fails. Pull-request and `workflow_dispatch` runs still only upload workflow artifacts (no GitHub Release).

Linux outputs are also uploaded as a workflow artifact named `realm-portal-linux-<version>` containing:

- `realm-portal_<deb-version>_amd64.deb` (SemVer prerelease dashes become `~` in the Debian version/filename, e.g. `1.1.0-rc.1` → `realm-portal_1.1.0~rc.1_amd64.deb`, so dpkg sorts the rc below the final release)
- `RealmPortal-<version>-x86_64.AppImage` (keeps the SemVer form, e.g. `…-1.1.0-rc.1-…`)
- `SHA256SUMS` (checksums for the `.deb` and AppImage)

Windows output is uploaded as `realm-portal-win-x64-<version>` containing `RealmPortal-<version>-win-x64.zip` and `SHA256SUMS`. The ZIP contains the self-contained `win-x64` publish output, including `RealmPortal.App.exe`.

AppStream metainfo: `build-packages.sh` writes a `<release>` entry for the package version; prerelease versions also set `type="development"`.

### Build steps (summary)

1. Restore the app project once with `--locked-mode`; its committed lock files include both `linux-x64` and `win-x64`. Each job then runs `dotnet publish --no-restore` for its RID as self-contained. Linux uses `linux-x64`, `PublishTrimmed=true`, `TrimMode=partial`, and `PublishReadyToRun=true`; Windows uses `win-x64` and `PublishReadyToRun=true`.
2. `packaging/linux/build-packages.sh` lays out the `.deb` and AppImage from that publish directory (see below).
3. `appimagetool` (AppImageKit release 13, pinned SHA in the workflow) produces the AppImage.

Local reproduction (after publish):

```bash
export APPIMAGETOOL=/path/to/obsolete-appimagetool-x86_64.AppImage
packaging/linux/build-packages.sh /path/to/publish "$(EVENT_NAME=workflow_dispatch packaging/resolve-version.sh)" ./artifacts/linux
```

## Install layout

Both packages install the self-contained publish output under `/opt/realm-portal` (`.deb`) or `usr/lib/realm-portal` inside the AppImage, and expose a `realm-portal` launcher on `PATH` that runs `RealmPortal.App`. This matches `Exec=realm-portal` in the desktop file.

Desktop entry, AppStream metainfo, and hicolor icons come from `packaging/linux/`. The source metainfo carries no version; `build-packages.sh` adds the `<releases>` entry for the package version (dated from `SOURCE_DATE_EPOCH` when set) and refuses a metainfo with a hardcoded `<releases>`.

### License and notice files

Release builds are self-contained, so they redistribute the .NET runtime. `packaging/collect-licenses.sh <publish-dir> <rid> <dest>`
reads the runtime pack version from `RealmPortal.App.deps.json` in the publish output and copies, from that exact pack in the NuGet
global packages folder, `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` into `licenses/dotnet-runtime/` (plus a `VERSION.txt` with the
runtime version and source commit). It also copies `LICENSE`, `THIRD_PARTY_LICENSES.md` and the fonts' `OFL.txt` (`licenses/fonts/`).
Nothing is committed for the runtime, so the notices always match the bundled patch release.

NuGet packages: the full license and notice texts of every package in the published `deps.json` are committed under
`licenses/nuget/<folder>/` and indexed in `licenses/nuget/packages.tsv`, one row per package: id, exact version, license, folder,
the comma-separated files expected in that folder, and the source of those files.

Folders are grouped by license source, not one per package: packages released together from the same upstream with the same
license files share one folder named after the family and version. For example, the nine `Avalonia.*` 11.3.22 packages share
`avalonia-11.3.22/`, `SkiaSharp` and its `NativeAssets.*` packages share `skiasharp-2.88.9/`, and the `HarfBuzzSharp` packages share
`harfbuzzsharp-8.3.1.1/`. A package with its own license source gets its own folder (e.g. `penet-5.1.0/`, `penet.asn1-2.0.1/`).
Rows that share a folder must list the same files.

The script lists the `libraries` of the publish `deps.json` and fails the build in these cases:
- a `package` entry has no exact id+version row;
- one of the files its row lists is missing or empty;
- its folder holds a file the row does not list;
- a required package (Avalonia, Avalonia.Angle.Windows.Natives, CommunityToolkit.Mvvm, HarfBuzzSharp, MicroCom.Runtime, PeNet,
  PeNet.Asn1, SkiaSharp, Tmds.DBus.Protocol) is absent.

Only our own projects (`RealmPortal.*`) and the `Microsoft.NETCore.App` runtime pack are accepted without a row. It copies the
listed files of the used folders to `licenses/nuget/` and writes `licenses/nuget/PACKAGES.txt` (the packages of that build).
`packaging/test-collect-licenses.sh` tests these rules in the Linux job: an unlisted package, a wrong version, a missing required
package, a single listed file deleted or emptied, and an unlisted extra file. In normal CI, `NuGetLicenseManifestTests` checks the
manifest against `src/RealmPortal.App/packages.lock.json` and `THIRD_PARTY_LICENSES.md`, and applies the same per-file rule to the
committed folders. A dependency bump without license files therefore fails on the PR.

When a package is added or bumped:
1. Copy its license and notice files into `licenses/nuget/`, from the `.nupkg` when it has them, otherwise from the upstream
   repository at the matching tag or commit. Use the shared family folder when the package comes from the same upstream release with
   the same files (e.g. a new `Avalonia.*` package at the same version); otherwise use a new `<family>-<version>/` folder.
2. Add or update its `packages.tsv` row, including the file list, and its row in `THIRD_PARTY_LICENSES.md`.
3. Delete folders that no row uses any more.

| Package | Location |
| --- | --- |
| `.deb` | `/usr/share/doc/realm-portal/` |
| AppImage | `usr/share/doc/realm-portal/` inside the image |
| Windows ZIP | next to `RealmPortal.App.exe` |

`build-packages.sh` fails if the `.deb` or the AppDir lacks `LICENSE`, the machine-readable Debian `copyright` file, `THIRD_PARTY_LICENSES.md`, the two runtime files or `licenses/nuget/PACKAGES.txt`, and the
Windows job checks the publish folder before zipping.

The `.deb` (and the AppImage AppDir) ships a DEP-5 / copyright-format 1.0 file at `/usr/share/doc/realm-portal/copyright` (`packaging/linux/copyright`). It covers Realm Portal (MIT), branding/icons art (MIT; see `assets/branding/LICENSE-ART.md`), and the bundled fonts (SIL OFL-1.1, with full texts under `licenses/fonts/`). Self-contained third-party notices for the .NET runtime and NuGet deps live under `/usr/share/doc/realm-portal/licenses/` and are inventoried in `THIRD_PARTY_LICENSES.md` / `licenses/nuget/PACKAGES.txt`.

### `.deb` dependencies

The control file declares runtime libraries expected on typical Debian/Ubuntu desktops (Avalonia/Skia/X11):

`Depends: libfontconfig1, libfreetype6, libx11-6, libice6, libsm6`

## Trimming

Publishing uses **partial** trimming to reduce size while keeping Avalonia viable. Expect IL2026/IL2104 analyzer warnings for:

- `System.Text.Json` in Core and localization (reflection-based serializers).
- Avalonia reflection bindings in a few places (most XAML uses compiled bindings).

`Directory.Build.props` sets `JsonSerializerIsReflectionEnabledByDefault=true` when `PublishTrimmed` is enabled so trimmed builds can still load JSON config and locale tables. Long term, source-generated `JsonSerializerContext` types would remove that trade-off.

CI fails only on publish **errors**, not trim warnings. Manual smoke tests on real desktops (e.g. Linux Mint) are still required after download.

## Window icon on X11

`MainWindow` sets the `_NET_WM_ICON` property itself once it is open (`RealmPortal.App.Platform.X11WindowIcon`, Linux/X11 only, best effort).
It writes all the natively drawn sizes (16, 22, 24, 32, 48, 64, 128 and 256 px: micro up to 24 px, simplified at 32 px, full from 48 px) so the window
manager can pick the right one for the panel, the task switcher and Alt-Tab, instead of scaling the single 256 px `Window.Icon` bitmap that
Avalonia sets. The PNGs are bundled from `assets/branding/icon/png` as `Assets/Icons/realm-portal-<N>.png`. Under Wayland (XWayland) the window is
still an X11 window and works the same; on a native Wayland session or without `libX11.so.6` nothing is written and the Avalonia icon stays.
Checked on a real X server (Xvfb) with `xprop -len 1000000 -name "Realm Portal" _NET_WM_ICON`.

## Desktop entry

- **`Exec=realm-portal`** is satisfied by the `realm-portal` launcher installed by the packages (see above).
- **`StartupWMClass=RealmPortal.App`** is set so docks can match the window (`WM_CLASS` / `res_class` is `RealmPortal.App` when started from the published executable).
- The `.desktop` icon name is `com.karloz7715.RealmPortal`; the hicolor icons under `packaging/linux/icons` follow that name.
