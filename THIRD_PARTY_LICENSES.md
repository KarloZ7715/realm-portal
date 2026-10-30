# Realm Portal — Third-party licenses

Realm Portal's own code is MIT (see `LICENSE`). This file lists third-party software and resources.

## NuGet packages shipped in the app (release packages)

The list is every `package` entry in `RealmPortal.App.deps.json` of the linux-x64 and win-x64 release publishes (same set on
both; trimming may drop some assemblies, but the packages are listed as published). Versions match `Directory.Packages.props`
and `src/RealmPortal.App/packages.lock.json` (checked 2026-10-29). The full license and notice texts are committed under
`licenses/nuget/<folder>/`, one folder per license source shared by packages of the same family (index:
`licenses/nuget/packages.tsv`, with the expected files and the source of each row) and shipped in every package
under `licenses/nuget/`.

| Package | Version | License | License files | Use |
|---|---|---|---|---|
| Avalonia | 11.3.22 | MIT | `licenses/nuget/avalonia-11.3.22/` | cross-platform UI |
| Avalonia.Desktop | 11.3.22 | MIT | `licenses/nuget/avalonia-11.3.22/` | Avalonia UI |
| Avalonia.FreeDesktop | 11.3.22 | MIT | `licenses/nuget/avalonia-11.3.22/` | Avalonia UI |
| Avalonia.Native | 11.3.22 | MIT | `licenses/nuget/avalonia-11.3.22/` | Avalonia UI |
| Avalonia.Remote.Protocol | 11.3.22 | MIT | `licenses/nuget/avalonia-11.3.22/` | Avalonia UI |
| Avalonia.Skia | 11.3.22 | MIT | `licenses/nuget/avalonia-11.3.22/` | Avalonia UI |
| Avalonia.Themes.Simple | 11.3.22 | MIT | `licenses/nuget/avalonia-11.3.22/` | Avalonia UI |
| Avalonia.Win32 | 11.3.22 | MIT | `licenses/nuget/avalonia-11.3.22/` | Avalonia UI |
| Avalonia.X11 | 11.3.22 | MIT | `licenses/nuget/avalonia-11.3.22/` | Avalonia UI |
| Avalonia.BuildServices | 11.3.2 | MIT | `licenses/nuget/avalonia.buildservices-11.3.2/` | Avalonia build-time package (no runtime files) |
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | BSD-3-Clause | `licenses/nuget/avalonia.angle.windows.natives-2.1.25547.20250602/` | ANGLE OpenGL ES on Direct3D (`av_libglesv2.dll`, win-x64) |
| BouncyCastle.Cryptography | 2.7.0 | MIT | `licenses/nuget/bouncycastle.cryptography-2.7.0/` | Ed25519 and BLAKE2b for release signature checks (`RealmPortal.Core`) |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | `licenses/nuget/communitytoolkit.mvvm-8.4.2/` | view-model helpers |
| SkiaSharp | 2.88.9 | MIT | `licenses/nuget/skiasharp-2.88.9/` | 2D rendering (Skia, native `libSkiaSharp`) |
| SkiaSharp.NativeAssets.Linux | 2.88.9 | MIT | `licenses/nuget/skiasharp-2.88.9/` | Skia native binaries |
| SkiaSharp.NativeAssets.Win32 | 2.88.9 | MIT | `licenses/nuget/skiasharp-2.88.9/` | Skia native binaries |
| SkiaSharp.NativeAssets.macOS | 2.88.9 | MIT | `licenses/nuget/skiasharp-2.88.9/` | Skia native binaries |
| SkiaSharp.NativeAssets.WebAssembly | 2.88.9 | MIT | `licenses/nuget/skiasharp-2.88.9/` | Skia native binaries |
| HarfBuzzSharp | 8.3.1.1 | MIT | `licenses/nuget/harfbuzzsharp-8.3.1.1/` | text shaping (HarfBuzz, native `libHarfBuzzSharp`) |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.1 | MIT | `licenses/nuget/harfbuzzsharp-8.3.1.1/` | HarfBuzz native binaries |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.1 | MIT | `licenses/nuget/harfbuzzsharp-8.3.1.1/` | HarfBuzz native binaries |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.1 | MIT | `licenses/nuget/harfbuzzsharp-8.3.1.1/` | HarfBuzz native binaries |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.1 | MIT | `licenses/nuget/harfbuzzsharp-8.3.1.1/` | HarfBuzz native binaries |
| MicroCom.Runtime | 0.11.0 | MIT | `licenses/nuget/microcom.runtime-0.11.0/` | Avalonia COM interop |
| PeNet | 5.1.0 | Apache-2.0 | `licenses/nuget/penet-5.1.0/` | reads the version resource of `Wow.exe` (`RealmPortal.Core`) |
| PeNet.Asn1 | 2.0.1 | Apache-2.0 | `licenses/nuget/penet.asn1-2.0.1/` | PeNet dependency (Authenticode parsing) |
| System.IO.Pipelines | 8.0.0 | MIT | `licenses/nuget/system.io.pipelines-8.0.0/` | Tmds.DBus dependency |
| System.Security.Cryptography.Pkcs | 9.0.21 | MIT | `licenses/nuget/system.security.cryptography.pkcs-9.0.21/` | PeNet dependency (pinned) |
| Tmds.DBus.Protocol | 0.21.3 | MIT | `licenses/nuget/tmds.dbus.protocol-0.21.3/` | Avalonia D-Bus (Linux) |

Notes:
- `skiasharp-2.88.9/THIRD-PARTY-NOTICES.txt` and `harfbuzzsharp-8.3.1.1/THIRD-PARTY-NOTICES.txt` (identical upstream files) carry
  the notices of the native code bundled in `libSkiaSharp` / `libHarfBuzzSharp`: Skia and etc1 (BSD-3-Clause), HarfBuzz ("Old
  MIT"), FreeType (FTL or GPLv2, as stated there), libpng, zlib, expat, libjpeg-turbo, libwebp, ICU, ANGLE, sfntly, piex, DNG SDK and others.
- `avalonia-11.3.22/NOTICE.md` lists third-party code included in Avalonia (WPF, SharpDX and others).
- ANGLE (BSD-3-Clause, © The ANGLE Project Authors) is bundled on win-x64 through `Avalonia.Angle.Windows.Natives`.
- PeNet.Asn1's nuspec declares Apache-2.0 while its repository's `LICENCE` is MIT; both texts are shipped. PeNet has no NOTICE file.

## Development and test only (not shipped)

| Package | Version | License |
|---|---|---|
| Avalonia.Headless, Avalonia.Headless.XUnit | 11.3.22 | MIT |
| Microsoft.NET.Test.Sdk, Microsoft.TestPlatform.ObjectModel, Microsoft.TestPlatform.TestHost, Microsoft.CodeCoverage | 18.11.0 | MIT |
| FsCheck, FsCheck.Xunit | 3.5.0 | BSD-3-Clause |
| FSharp.Core | 5.0.2 | MIT |
| xunit, xunit.core, xunit.assert, xunit.extensibility.core, xunit.extensibility.execution | 2.9.3 | Apache-2.0 |
| xunit.abstractions | 2.0.3 | Apache-2.0 (license URL) |
| xunit.analyzers | 1.18.0 | Apache-2.0 |
| xunit.runner.visualstudio | 4.0.1 | Apache-2.0 |

Keep these tables in sync with `Directory.Packages.props`; `NuGetLicenseManifestTests` fails when a shipped package lacks a row
in `licenses/nuget/packages.tsv` or here, and `packaging/collect-licenses.sh` fails the release build for any published package
without license files.

## .NET runtime (bundled in release builds)

This source repository does not vendor the .NET SDK or runtime, but the release binaries are self-contained: the
`.deb`, the AppImage and the Windows ZIP built by `.github/workflows/release-linux.yml` bundle the .NET runtime.

| Component | Version | License | Use |
|---|---|---|---|
| .NET runtime (`Microsoft.NETCore.App`, runtime pack `Microsoft.NETCore.App.Runtime.linux-x64` / `.win-x64`) | the 9.0 runtime bundled at build time (latest 9.0.x patch of the SDK installed by the workflow, `dotnet-version: 9.0.x`; SDK band per `global.json`) | MIT (© .NET Foundation and Contributors), plus the third-party notices in its `THIRD-PARTY-NOTICES.TXT` | runtime of the self-contained app (release builds only) |

ASP.NET Core and the Windows Desktop runtime (`Microsoft.WindowsDesktop.App`) are not used and not bundled.

Every release package ships the runtime's `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT`, copied by
`packaging/collect-licenses.sh` from the exact runtime pack that `dotnet publish` bundled, together with
`licenses/dotnet-runtime/VERSION.txt` (exact runtime version and source commit), this file, `LICENSE` and the fonts'
`OFL.txt`. Locations: `/usr/share/doc/realm-portal/` once the `.deb` is installed; `usr/share/doc/realm-portal/` inside the
AppImage (view with `--appimage-extract`); next to `RealmPortal.App.exe` in the Windows ZIP. The NuGet license folders above
ship in the same place, under `licenses/nuget/`, with a per-build `licenses/nuget/PACKAGES.txt`.


Texture, typeface and drawing assets (registered on 2026-10-17, updated with the loose assets in `assets/`). The textures informed the project's "Parchment and bronze" visual direction.

## Textures (ambientCG, CC0 1.0 Universal)
All ambientCG textures are published under CC0 1.0 (https://docs.ambientcg.com/license/): they may be copied, modified and distributed, including commercially, without asking permission or giving credit. The source is cited anyway.

| Use | Asset | Asset URL | Official download (1K-JPG) | License | Map used | Derived file |
|---|---|---|---|---|---|---|
| Leather (window background, sidebar, buttons) | Leather 037 | https://ambientcg.com/a/Leather037 | https://ambientcg.com/get?file=Leather037_1K-JPG.zip | CC0 1.0 | `Displacement` (the color map is flat), converted to grayscale and tinted dark brown | `assets/textures/tex-leather.jpg` |
| Paper (parchment of panels and cards) | Paper 003 | https://ambientcg.com/a/Paper003 | https://ambientcg.com/get?file=Paper003_1K-JPG.zip | CC0 1.0 | `Color` in grays, contrast reduced to 50% and tinted parchment | `assets/textures/tex-parchment.jpg` |
| Bronze (scrollbar thumbs and progress fill of the UI kit; not used by the app icon) | Metal 008 | https://ambientcg.com/a/Metal008 | https://ambientcg.com/get?file=Metal008_1K-JPG.zip | CC0 1.0 | `Color`, grayscale tinted muted bronze, reduced to 512² | `assets/textures/tex-bronze.jpg` |

Processed with Python/Pillow (autocontrast, colorize, blend). Only the derivatives in `assets/textures/` are included in the repository.

## Typefaces (SIL Open Font License 1.1, Google Fonts)
Downloaded on 2026-10-17 from the official google/fonts repository (`ofl/` folder), with each family's `OFL.txt`, in `assets/fonts/<family>/`. Details and notes in `assets/fonts/FONTS.md`. They may be embedded and redistributed with the app; they are not sold separately; modifications may not use the Reserved Font Name (RFN) when one exists.

| Family | Files in `assets/fonts/` | Copyright | RFN | Source |
|---|---|---|---|---|
| Almendra (Bold) | `almendra/Almendra-Bold.ttf`, `OFL.txt`, `FONTLOG.txt` | © 2011-2012 Ana Sanfelippo | "Almendra" (file unmodified) | https://github.com/google/fonts/tree/main/ofl/almendra |
| Marcellus SC | `marcellussc/MarcellusSC-Regular.ttf`, `OFL.txt` | © 2012 Brian J. Bonislawsky DBA Astigmatic (AOETI) | "Marcellus" (file unmodified) | https://github.com/google/fonts/tree/main/ofl/marcellussc |
| Alegreya | `alegreya/Alegreya[wght].ttf` (original variable font), `Alegreya-Regular/Medium/Bold.ttf` (static instances derived with fontTools), `OFL.txt` | © 2011 The Alegreya Project Authors | none | https://github.com/google/fonts/tree/main/ofl/alegreya |
| Alegreya Sans | `alegreyasans/AlegreyaSans-Regular/Medium/Bold.ttf` (unmodified), `OFL.txt` | © 2013 The Alegreya Sans Project Authors | none | https://github.com/google/fonts/tree/main/ofl/alegreyasans |
| JetBrains Mono | `jetbrainsmono/JetBrainsMono[wght].ttf` (original variable font), `JetBrainsMono-Regular.ttf` (static instance derived with fontTools), `OFL.txt` | © 2020 The JetBrains Mono Project Authors | none | https://github.com/google/fonts/tree/main/ofl/jetbrainsmono |

Bundled in the app as resources (`src/RealmPortal.App/RealmPortal.App.csproj`, linked from `assets/fonts/`): the static TTFs only (Almendra Bold, Marcellus SC, Alegreya Regular/Medium/Bold, Alegreya Sans Regular/Medium/Bold, JetBrains Mono Regular) plus each family's `OFL.txt` under `Assets/Fonts/licenses/`. The variable fonts are not bundled. The Medium weights are registered under their own family names ("Alegreya Medium", "Alegreya Sans Medium").

The static instances of Alegreya and JetBrains Mono are derivative works permitted by the OFL (no RFN); they are distributed with the same `OFL.txt`.

## Loose generated assets (`assets/` folder, 2026-10-17)

| Group | Files | License / provenance |
|---|---|---|
| `assets/textures/` | `tex-parchment.jpg` (Paper003), `tex-leather.jpg` (Leather037), `tex-bronze.jpg` (Metal008) | Derived from the ambientCG textures in the table above, **CC0 1.0**. Re-encoded as JPEG q90, no other changes (already tileable; verified in a 2×2 tiling). |
| `assets/ui/` | UI kit: 130 pieces (PNG @1x/@2x, flat SVG) and `nineslice.json` v2: parchment panels, leather buttons, split plates, focus rings, fields, popups, scrollbar, progress, checkbox, radio, switch, shadow | Drawn from scratch for this project (Python, Pillow, cairosvg). **MIT**. The leather buttons and `popup-leather` bake in the CC0 **Leather037** texture; scrollbar thumbs and `progress-fill` (leather variant) bake in the CC0 **Metal008** texture (25 %), both ambientCG, CC0 1.0 (see the textures table above). The parchment grain is procedural (own work). No fonts included. No Blizzard assets. |
| `assets/frames/` (PNG/SVG) | parchment panels, bronze corners, buttons, rivet hardware, focus rings, window frame, divider, stitching, tab | Drawn from scratch for this project with Python (Pillow + cairosvg), using the geometry and tokens of the project's own mockups. The PNGs have the CC0 textures above baked in (parchment, leather), so those parts remain **CC0 1.0** (ambientCG, no attribution required). The SVGs are flat, with no third-party material. |
| `assets/branding/` and `packaging/linux/icons/` | Realm Portal app icon ("Tesera"): SVG, PNG 16–512, `.ico`, Linux hicolor and symbolic | Original design for this project, drawn as geometric vectors (no textures, fonts or third-party artwork). **MIT**, see `assets/branding/LICENSE-ART.md`. Trademark clearance pending. Not affiliated with or endorsed by Blizzard Entertainment. |
| `assets/icons/seals/` | 5 status seals, SVG and PNG 64/128 | Own design; no third parties. |
| `assets/icons/ui/`, `assets/icons/ui-ink/` | 23 interface icons as SVG | Hand-drawn for this project with a project script (not included). They do **not** come from the Figma connector or from Lucide (ISC), Feather, Heroicons or any other library; therefore there is no third-party attribution. |
| `assets/fonts/` | TTF + OFL.txt | See the typefaces section (OFL 1.1). |
| `assets/preview-composition.png`, `assets/textures/preview-tiling-2x2.png` | check images | Compositions of the assets above with the OFL typefaces; no additional third parties. |

The project's own drawings (frames, icons, seals, app icon) are published under the repository's MIT license (see `LICENSE`).

## No Blizzard assets
No Blizzard logo, trademark, screenshot or typeface is used. The app icon (split-coin "Tesera" with a butterfly key), the status seals and the ornaments are own vectors drawn for this project.
