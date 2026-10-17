# Realm Portal — Licencias de terceros / Third-party licenses

El código de Realm Portal es MIT (`LICENSE`). Este documento lista el software y los recursos de terceros. / Realm Portal's own code is MIT (`LICENSE`). This file lists third-party software and resources.

## Paquetes NuGet / NuGet packages (versiones fijadas en `Directory.Packages.props`)

Verificado en los `.nuspec` de los paquetes restaurados (Fase 1, 2026-10-17). / Checked against the restored packages' `.nuspec` files.

**Dependencias directas / Direct**

| Paquete | Versión | Licencia | Uso |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent | 11.3.22 | MIT (© The AvaloniaUI Project) | UI multiplataforma (`RealmPortal.App`) |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT | pruebas (solo desarrollo) |
| xunit | 2.9.3 | Apache-2.0 | pruebas (solo desarrollo) |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 | pruebas (solo desarrollo) |

**Transitivas notables / Notable transitive**

| Paquete | Versión | Licencia |
|---|---|---|
| Avalonia.Skia, Avalonia.X11, Avalonia.Win32, Avalonia.Native, Avalonia.FreeDesktop, Avalonia.Remote.Protocol, Avalonia.BuildServices | 11.3.x | MIT |
| SkiaSharp (+ NativeAssets) | 2.88.9 | MIT (Skia: BSD-3-Clause) |
| HarfBuzzSharp (+ NativeAssets) | 8.3.1.1 | MIT (HarfBuzz: MIT) |
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | BSD-3-Clause (© The ANGLE Project Authors), solo Windows |
| MicroCom.Runtime | 0.11.0 | MIT |
| Tmds.DBus.Protocol | 0.21.3 | MIT |
| System.IO.Pipelines | 8.0.0 | MIT |
| Microsoft.TestPlatform.*, Microsoft.CodeCoverage, Newtonsoft.Json 13.0.1 | 17.12.0 / 13.0.1 | MIT (solo pruebas) |
| xunit.core / .assert / .extensibility.* / .analyzers / .abstractions | 2.9.3 / 1.18.0 / 2.0.3 | Apache-2.0 (.abstractions: ver su licencia upstream) (solo pruebas) |
| System.Reflection.Metadata | 1.6.0 | MIT (solo pruebas) |

El SDK y el runtime de .NET (MIT) no se redistribuyen en este repositorio. / The .NET SDK/runtime (MIT) is not redistributed in this repository.

Mantener esta tabla al día cuando cambie `Directory.Packages.props`. / Keep this table in sync with `Directory.Packages.props`.


Fecha de registro: 2026-10-17 (actualizado con los assets sueltos de `assets/`). Texturas usadas en las maquetas de Figma «Realm Portal — Maquetas B (Pergamino y bronce)» (https://www.figma.com/design/6kBPmTavrfpbceMSUvvWl3).

## Texturas (ambientCG, CC0 1.0 Universal)
Todas las texturas de ambientCG se publican bajo CC0 1.0 (https://docs.ambientcg.com/license/): se pueden copiar, modificar y distribuir, incluso con fines comerciales, sin pedir permiso ni dar crédito. Aun así se cita la fuente.

| Uso | Asset | URL del asset | Descarga oficial (1K-JPG) | Licencia | Mapa usado | Archivo derivado |
|---|---|---|---|---|---|---|
| Cuero (fondo de ventana, barra lateral, botones) | Leather 037 | https://ambientcg.com/a/Leather037 | https://ambientcg.com/get?file=Leather037_1K-JPG.zip | CC0 1.0 | `Displacement` (el mapa de color es plano), pasado a escala de grises y coloreado en marrón oscuro | `assets/textures/tex-cuero.jpg` |
| Papel (pergamino de paneles y tarjetas) | Paper 003 | https://ambientcg.com/a/Paper003 | https://ambientcg.com/get?file=Paper003_1K-JPG.zip | CC0 1.0 | `Color` en grises, contraste reducido al 50 % y coloreado en tono pergamino | `assets/textures/tex-pergamino.jpg` |
| Bronce (aro del icono de la app) | Metal 008 | https://ambientcg.com/a/Metal008 | https://ambientcg.com/get?file=Metal008_1K-JPG.zip | CC0 1.0 | `Color`, escala de grises coloreada en bronce apagado, reducida a 512² | `assets/textures/tex-bronce.jpg` |

Procesado con Python/Pillow (autocontraste, colorize, mezcla). Solo se incluyen en el repositorio los derivados de `assets/textures/`.

## Tipografías (SIL Open Font License 1.1, Google Fonts)
Descargadas el 2026-10-17 del repositorio oficial google/fonts (carpeta `ofl/`), con el `OFL.txt` de cada familia, en `assets/fonts/<familia>/`. Detalle y notas en `assets/fonts/FONTS.md`. Pueden incrustarse y redistribuirse con la app; no se venden por separado; las modificaciones no pueden usar el Reserved Font Name (RFN) cuando existe.

| Familia | Archivos en `assets/fonts/` | Copyright | RFN | Origen |
|---|---|---|---|---|
| Almendra (Bold) | `almendra/Almendra-Bold.ttf`, `OFL.txt`, `FONTLOG.txt` | © 2011-2012 Ana Sanfelippo | «Almendra» (archivo sin modificar) | https://github.com/google/fonts/tree/main/ofl/almendra |
| Marcellus SC | `marcellussc/MarcellusSC-Regular.ttf`, `OFL.txt` | © 2012 Brian J. Bonislawsky DBA Astigmatic (AOETI) | «Marcellus» (archivo sin modificar) | https://github.com/google/fonts/tree/main/ofl/marcellussc |
| Alegreya | `alegreya/Alegreya[wght].ttf` (original variable), `Alegreya-Regular/Medium/Bold.ttf` (instancias estáticas derivadas con fontTools), `OFL.txt` | © 2011 The Alegreya Project Authors | ninguno | https://github.com/google/fonts/tree/main/ofl/alegreya |
| Alegreya Sans | `alegreyasans/AlegreyaSans-Regular/Medium/Bold.ttf` (sin modificar), `OFL.txt` | © 2013 The Alegreya Sans Project Authors | ninguno | https://github.com/google/fonts/tree/main/ofl/alegreyasans |
| JetBrains Mono | `jetbrainsmono/JetBrainsMono[wght].ttf` (original variable), `JetBrainsMono-Regular.ttf` (instancia estática derivada con fontTools), `OFL.txt` | © 2020 The JetBrains Mono Project Authors | ninguno | https://github.com/google/fonts/tree/main/ofl/jetbrainsmono |

Las instancias estáticas de Alegreya y JetBrains Mono son obras derivadas permitidas por la OFL (sin RFN); se distribuyen con el mismo `OFL.txt`.

## Assets sueltos generados (carpeta `assets/`, 2026-10-17)

| Grupo | Archivos | Licencia / procedencia |
|---|---|---|
| `assets/textures/` | `tex-pergamino.jpg` (Paper003), `tex-cuero.jpg` (Leather037), `tex-bronce.jpg` (Metal008) | Derivados de las texturas ambientCG de la tabla anterior, **CC0 1.0**. Recodificados a JPEG q90, sin más cambios (ya eran teselables; verificado en mosaico 2×2). |
| `assets/frames/` (PNG/SVG) | paneles de pergamino, esquineros de bronce, botones, herraje con remaches, anillos de foco, marco de ventana, divisor, costura, pestaña | Dibujados desde cero para este proyecto con Python (Pillow + cairosvg), con geometría y tokens de las maquetas propias. Los PNG llevan horneadas las texturas CC0 anteriores (pergamino, cuero), de modo que esas partes siguen **CC0 1.0** (ambientCG, sin obligación de atribución). Los SVG son planos, sin material de terceros. |
| `assets/icons/app/` | `app-icon*.svg`, `app-icon-{512,256,128,64,48}.png`, `app-icon-simple-{32,16}.png`, `app-icon.ico` | Diseño propio (sello de lacre con arco-portal). Los PNG ≥64 px llevan el aro con la textura Metal008 (**CC0 1.0**). |
| `assets/icons/sellos/` | 5 sellos de estado, SVG y PNG 64/128 | Diseño propio; sin terceros. |
| `assets/icons/ui/`, `assets/icons/ui-tinta/` | 23 iconos de interfaz SVG | Dibujados a mano para este proyecto (un script del proyecto (no incluido)). **No** proceden del conector de Figma ni de Lucide (ISC), Feather, Heroicons u otra librería; por tanto no hay atribución de terceros. |
| `assets/fonts/` | TTF + OFL.txt | Ver sección de tipografías (OFL 1.1). |
| `assets/preview-composicion.png`, `assets/textures/preview-mosaico-2x2.png` | imágenes de comprobación | Composiciones de los assets anteriores con las fuentes OFL; sin terceros adicionales. |

Los dibujos propios (frames, iconos, sellos, icono de app) se publican bajo la licencia MIT del repositorio (ver `LICENSE`).

## Sin assets de Blizzard
No se usa ningún logotipo, marca, captura ni fuente de Blizzard. El icono (sello de lacre con arco-portal), los sellos de estado y los ornamentos son vectores propios dibujados para este proyecto.
