# Realm Portal — Third-party licenses

Realm Portal's own code is MIT (see `LICENSE`). This file lists third-party software and resources.

## NuGet packages (versions pinned in `Directory.Packages.props`)

Checked against the restored packages' `.nuspec` files (Phase 1, 2026-10-17).

**Direct dependencies**

| Package | Version | License | Use |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent | 11.3.22 | MIT (© The AvaloniaUI Project) | cross-platform UI (`RealmPortal.App`) |
| Avalonia.Headless, Avalonia.Headless.XUnit, Avalonia.Skia | 11.3.22 | MIT (© The AvaloniaUI Project) | headless UI tests and screenshots (development only) |
| PeNet | 5.1.0 | Apache-2.0 | reads the version resource of `Wow.exe` (`RealmPortal.Core`) |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT | tests (development only) |
| xunit | 2.9.3 | Apache-2.0 | tests (development only) |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 | tests (development only) |

**Notable transitive dependencies**

| Package | Version | License |
|---|---|---|
| Avalonia.Skia, Avalonia.X11, Avalonia.Win32, Avalonia.Native, Avalonia.FreeDesktop, Avalonia.Remote.Protocol, Avalonia.BuildServices | 11.3.x | MIT |
| SkiaSharp (+ NativeAssets) | 2.88.9 | MIT (Skia: BSD-3-Clause) |
| HarfBuzzSharp (+ NativeAssets) | 8.3.1.1 | MIT (HarfBuzz: MIT) |
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | BSD-3-Clause (© The ANGLE Project Authors), Windows only |
| PeNet.Asn1 | 2.0.1 | Apache-2.0 |
| System.Security.Cryptography.Pkcs (PeNet dependency) | 8.0.1 | MIT |
| MicroCom.Runtime | 0.11.0 | MIT |
| Tmds.DBus.Protocol | 0.21.3 | MIT |
| System.IO.Pipelines | 8.0.0 | MIT |
| Microsoft.TestPlatform.*, Microsoft.CodeCoverage, Newtonsoft.Json 13.0.1 | 17.12.0 / 13.0.1 | MIT (tests only) |
| xunit.core / .assert / .extensibility.* / .analyzers / .abstractions | 2.9.3 / 1.18.0 / 2.0.3 | Apache-2.0 (.abstractions: see its upstream license) (tests only) |
| System.Reflection.Metadata | 1.6.0 | MIT (tests only) |

The .NET SDK/runtime (MIT) is not redistributed in this repository.

Keep this table in sync with `Directory.Packages.props`.


Texture, typeface and drawing assets (registered on 2026-10-17, updated with the loose assets in `assets/`). Textures were used in the Figma mockups for design direction B "Parchment and bronze" (https://www.figma.com/design/6kBPmTavrfpbceMSUvvWl3).

## Textures (ambientCG, CC0 1.0 Universal)
All ambientCG textures are published under CC0 1.0 (https://docs.ambientcg.com/license/): they may be copied, modified and distributed, including commercially, without asking permission or giving credit. The source is cited anyway.

| Use | Asset | Asset URL | Official download (1K-JPG) | License | Map used | Derived file |
|---|---|---|---|---|---|---|
| Leather (window background, sidebar, buttons) | Leather 037 | https://ambientcg.com/a/Leather037 | https://ambientcg.com/get?file=Leather037_1K-JPG.zip | CC0 1.0 | `Displacement` (the color map is flat), converted to grayscale and tinted dark brown | `assets/textures/tex-leather.jpg` |
| Paper (parchment of panels and cards) | Paper 003 | https://ambientcg.com/a/Paper003 | https://ambientcg.com/get?file=Paper003_1K-JPG.zip | CC0 1.0 | `Color` in grays, contrast reduced to 50% and tinted parchment | `assets/textures/tex-parchment.jpg` |
| Bronze (ring of the app icon) | Metal 008 | https://ambientcg.com/a/Metal008 | https://ambientcg.com/get?file=Metal008_1K-JPG.zip | CC0 1.0 | `Color`, grayscale tinted muted bronze, reduced to 512² | `assets/textures/tex-bronze.jpg` |

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
| `assets/frames/` (PNG/SVG) | parchment panels, bronze corners, buttons, rivet hardware, focus rings, window frame, divider, stitching, tab | Drawn from scratch for this project with Python (Pillow + cairosvg), using the geometry and tokens of the project's own mockups. The PNGs have the CC0 textures above baked in (parchment, leather), so those parts remain **CC0 1.0** (ambientCG, no attribution required). The SVGs are flat, with no third-party material. |
| `assets/icons/app/` | `app-icon*.svg`, `app-icon-{512,256,128,64,48}.png`, `app-icon-simple-{32,16}.png`, `app-icon.ico` | Own design (wax seal with portal arch). PNGs ≥64 px carry the ring with the Metal008 texture (**CC0 1.0**). |
| `assets/icons/seals/` | 5 status seals, SVG and PNG 64/128 | Own design; no third parties. |
| `assets/icons/ui/`, `assets/icons/ui-ink/` | 23 interface icons as SVG | Hand-drawn for this project with a project script (not included). They do **not** come from the Figma connector or from Lucide (ISC), Feather, Heroicons or any other library; therefore there is no third-party attribution. |
| `assets/fonts/` | TTF + OFL.txt | See the typefaces section (OFL 1.1). |
| `assets/preview-composition.png`, `assets/textures/preview-tiling-2x2.png` | check images | Compositions of the assets above with the OFL typefaces; no additional third parties. |

The project's own drawings (frames, icons, seals, app icon) are published under the repository's MIT license (see `LICENSE`).

## No Blizzard assets
No Blizzard logo, trademark, screenshot or typeface is used. The icon (wax seal with portal arch), the status seals and the ornaments are own vectors drawn for this project.
