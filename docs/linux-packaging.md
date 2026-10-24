# Release packaging notes (F6)

## CI workflow

The [Release (Linux and Windows packages)](https://github.com/KarloZ7715/realm-portal/actions/workflows/release-linux.yml) workflow (`.github/workflows/release-linux.yml`) builds Linux packages and a Windows archive:

The version has a single source: `<Version>` in `Directory.Build.props`. `packaging/resolve-version.sh` reads it (self-tested by
`packaging/test-resolve-version.sh` in the Linux job) and both jobs pass the result to `dotnet publish` as `-p:Version` /
`-p:InformationalVersion`, so the package name, the `.deb` `Version:` field, the generated AppStream `<releases>` entry, the
status bar and `realm-portal --version` all agree.

| Trigger | Version used |
| --- | --- |
| `pull_request` (path-filtered) | `0.0.0-pr.<PR number>` (artifacts only; no release; the props version is still read and validated). It stays below every real release so an installed PR `.deb` never blocks an upgrade |
| `workflow_dispatch` | `<props version>` when the `version` input is empty; otherwise the input, which must be `<props version>` or a prerelease of it (`<props version>-rc.1`) |
| Push tag matching `v[0-9]+.[0-9]+.[0-9]+*` | Tag without the `v` prefix; it **must equal** `<props version>` (e.g. `v1.0.0` with `<Version>1.0.0</Version>`), otherwise the run fails before building |

To release: bump `<Version>` in `Directory.Build.props` in a PR, merge it, then push the matching `v<version>` tag.

It does **not** create a GitHub Release. Linux outputs are uploaded as a workflow artifact named `realm-portal-linux-<version>` containing:

- `realm-portal_<version>_amd64.deb`
- `RealmPortal-<version>-x86_64.AppImage`
- `SHA256SUMS` (checksums for the `.deb` and AppImage)

Windows output is uploaded as `realm-portal-win-x64-<version>` containing `RealmPortal-<version>-win-x64.zip` and `SHA256SUMS`. The ZIP contains the self-contained `win-x64` publish output, including `RealmPortal.App.exe`.

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
