# Realm Portal — Loose assets for design direction B "Parchment and bronze"

Generated on 2026-10-17 for the Avalonia styling spike. Visual source: the project's Figma mockups (https://www.figma.com/design/6kBPmTavrfpbceMSUvvWl3) and the design tokens derived from them. All PNGs in `frames/` and `icons/` are RGBA (real transparency, straight alpha, no halos verified on white, black, magenta and leather backgrounds).

Overview: `preview-composition.png` (9-slice window on magenta, active card, buttons, divider, tab, seals and corners; below, the same elements on white/black/magenta to spot halos).

## Layout
```
assets/
  ASSETS.md                  this document
  preview-composition.png    test composition
  textures/                  3 tileable CC0 JPG textures + tiling check image
  frames/                    frames, panels, buttons, dividers (PNG @1x/@2x + SVG) + nineslice.json
  ui/                        UI kit for the Avalonia theme (114 pieces + nineslice.json v2); see ui/README.md
  branding/                  app icon "Tesera" (SVG, PNG 16–512, ICO) — see Branding section
  icons/seals/               5 status seals (SVG, PNG 64/128)
  icons/ui/, icons/ui-ink/   23 interface icons as SVG (bone color / ink color)
  fonts/                     TTF + OFL.txt per family + FONTS.md
```

## Conventions
- **@1x / @2x**: `name.png` and `name@2x.png` (exactly double the pixels; each scale is rendered natively, not resampled, to avoid halos).
- **9-slice insets** are given as **left, top, right, bottom** in pixels of the @1x PNG (multiply by 2 for @2x). They are also in `frames/nineslice.json` (machine-readable).
- Avalonia has no native 9-slice for images (not tested here): a custom control or `DrawingContext.DrawImage` with source/destination rectangles for each of the 9 zones will be needed. The insets are designed for that.
- **SVGs in `frames/`** are **flat** vector versions (no texture, shadows or filters) for anyone who prefers vectors; the full look (texture, shadow, inner shadow) exists only in the PNGs.
- License of the drawings (SVG/PNG in frames and icons): created from scratch for this project and released under the repository's MIT license, with no third-party material except the CC0 textures baked into the PNGs (see `THIRD_PARTY_LICENSES.md`). No Blizzard assets.

## 1. textures/ (CC0 1.0, ambientCG)

| File | Pixels | Size | Tileable | Source / license |
|---|---|---|---|---|
| `textures/tex-parchment.jpg` | 1024×1024 | 44.8 KB | yes; edge-jump to neighbouring-gradient ratio 2.0× but only 0.65 gray levels; no visible seam in a 2×2 tiling | Paper003 (ambientCG) · CC0 1.0 |
| `textures/tex-leather.jpg` | 1024×1024 | 229.9 KB | yes; ratio 1.2×, no visible seam in a 2×2 tiling | Leather037 (ambientCG) · CC0 1.0 |
| `textures/tex-bronze.jpg` | 512×512 | 67.3 KB | yes; ratio 1.2–1.3×, no visible seam in a 2×2 tiling | Metal008 (ambientCG) · CC0 1.0 |
| `textures/preview-tiling-2x2.png` | 1840×600 | 889.2 KB | — | visual check (leather, parchment and bronze in a 2×2 tiling) |

Notes: the three textures were already tileable at the source (ambientCG publishes them that way) and are unmodified relative to the ambientCG originals (only re-encoded as JPEG q90). Verified with a 2×2 tiling (with ×2.2 contrast at the junction) and with the edge-jump metric against the interior gradient. In the mockups they are used with partial opacity over the token base color: parchment 50% over `#D8C7A0` (512 css px tile), leather 34–40% (512 css px tile), bronze 35% in Figma (the app icon no longer uses it; the UI kit bakes it at 25% into scrollbar thumbs and the progress fill). The parchment has large recognizable blotches: a 2×2 repetition shows the pattern; in panels up to ~700 px it does not repeat.

## 2. frames/

Insets are in @1x px (left, top, right, bottom). "Canvas" = size of the @1x PNG.

| File (PNG @1x + @2x, flat SVG) | Canvas @1x | 9-slice insets | What it is / notes |
|---|---|---|---|
| `panel-sheet-wide` | 724×134 | 36, 36, 36, 36 | shadow included in the PNG (12 px margin); the SVG is flat, without shadow or texture |
| `panel-sheet-medium` | 384×284 | 36, 36, 36, 36 | shadow included in the PNG (12 px margin); the SVG is flat, without shadow or texture |
| `panel-active-wide` | 724×134 | 36, 36, 36, 36 | shadow included in the PNG (12 px margin); the SVG is flat, without shadow or texture |
| `panel-active-medium` | 384×284 | 36, 36, 36, 36 | shadow included in the PNG (12 px margin); the SVG is flat, without shadow or texture |
| `corner-bronze-*` | 18×18 | — | no 9-slice; place at each panel corner at 0,0 (no margin) |
| `button-primary-{normal,hover,pressed,disabled}` | 160×40 | 14, 14, 14, 14 | leather background with baked-in texture, 1.5 px bronze border, dotted stitching and 2 rivets; the center stretches (the stitching stretches ~1.3x at 200 px wide) |
| `button-secondary-{normal,hover,pressed,disabled}` | 160×40 | 14, 14, 14, 14 | same as the primary buttons, wood tint instead of wax-red |
| `bronze-hardware-rivets` | 160×64 | 16, 16, 16, 16 | transparent center; use as a card/button border over any background; rivets at the 4 corners |
| `focus-ring-leather` | 176×56 | 14, 14, 14, 14 | focus ring (double on leather: dark outline + light focus); 160×40 element centered with an 8 px margin |
| `focus-ring-parchment` | 176×56 | 14, 14, 14, 14 | focus ring (single on parchment); same geometry as the leather one |
| `window-frame` | 960×600 | 14, 40, 14, 28 | opaque leather with baked-in title bar (40 px) and status bar (28 px), bronze trim and 4 rivets; the center is tiled leather (no visible seams when stretched) |
| `divider-thread-medallion` | 712×14 | — | horizontal 3-slice: left thread (stretchable, x 0–326), fixed medallion (central 44 px), right thread (stretchable); in Avalonia, better to use 2 tiled threads + `divider-medallion.png` centered |
| `stitch-h-tile` | 10×4 | — | repeatable tile (10 px period: 6 thread + 4 gap), color `#8A6C4A` |
| `stitch-v-tile` | 4×10 | — | repeatable tile (10 px period: 6 thread + 4 gap), color `#8A6C4A` |
| `tab-parchment` | 228×64 | 18, 18, 18, 18 | active sidebar tab: flush with the left edge (x=10 of the PNG = window edge); 5 px wax-red mark on the left |
| `divider-medallion` | 44×14 | — | loose bronze medallion (to center over two tiled threads) |

Per-piece details:

- **Parchment panels** (`panel-sheet-*` and `panel-active-*`): *wide* (body 700×110) and *medium* (body 360×260) shapes as in the mockup; they include a 12 px transparent margin for the shadow, an irregular edge (±1.4 px), an inner shadow and an inner dark-bronze rule at 6 px. `active` = sheet + 4 bronze corner pieces already baked in. Insets 36 (= 12 margin + 24 decorated zone) on all four sides. When stretched, the irregular edge of the middle zones stretches with the panel (smoother waviness): *wide* is meant for landscape shapes (≥ 200×60) and *medium* for squarer ones; below 72 px per side the insets overlap.
- **Bronze corners**: 4 separate PNGs (`-tl,-tr,-bl,-br`, 18×18) and `corner-bronze.svg` (TL, rotatable by 90/180/270). They are placed at the panel corner at (0,0) of the body (not of the canvas with shadow: add 12 px on panels).
- **Buttons**: 160×40; leather background with baked-in texture (wax-red tint = primary, wood = secondary), 1.5 px bronze border, inner dotted stitching (3/3) and 2 rivets. States normal/hover/pressed/disabled (disabled has no stitching or rivets, as in the mockup). Text and icon are NOT baked in. The stitching stretches with the 9-slice (≈1.3× at 200 px); if that bothers, use the flat SVG or draw the stitching in code. "Focus" is a separate piece (`focus-ring-*`, 8 px margin around the element).
- **`bronze-hardware-rivets`**: frame with a transparent center (bronze border, stitching, 4 rivets) for cards or buttons over any background. In the mockup the cards are parchment with corner pieces; this hardware is a coherent extension of the look, not a literal piece of the mockup.
- **`window-frame`**: opaque 960×600 (tiled leather), 40 px title bar and 28 px status bar baked in (no text or buttons), 2 px dark-bronze rule, 4 rivets and 8 px rounded (transparent) corners. Insets 14, 40, 14, 28. Note: the 2 px rule and the rivets sit inside the 14 px corners; the rounded corners require keeping those 14 px unstretched.
- **Divider**: `divider-thread-medallion` 712×14 is a horizontal 3-slice (left thread 0–326, fixed 44 px medallion at 334–378, right thread 386–712). For arbitrary widths, use `stitch-h-tile` repeated on each side of `divider-medallion`.
- **Dotted stitching**: repeatable tiles of 10×4 (horizontal) and 4×10 (vertical); 10 px period (6 thread + 4 gap), color `#8A6C4A`. At @1x the 1.5 px stroke lands on half a pixel (max alpha 192): this is expected; at @2x it is crisp.
- **`tab-parchment`**: active sidebar tab, 208×44 body with a 10 px shadow margin, right corners rounded 6 px, 5 px wax-red mark on the left; insets 18. It sits flush with the left edge (x=10 of the PNG = window edge).

Full listing of `frames/`:

| File | Pixels | Size |
|---|---|---|
| `frames/bronze-hardware-rivets.png` | 160×64 | 1.1 KB |
| `frames/bronze-hardware-rivets.svg` | SVG | 0.6 KB |
| `frames/bronze-hardware-rivets@2x.png` | 320×128 | 2.7 KB |
| `frames/button-primary-disabled.png` | 160×40 | 8.4 KB |
| `frames/button-primary-disabled@2x.png` | 320×80 | 26.2 KB |
| `frames/button-primary-hover.png` | 160×40 | 9.6 KB |
| `frames/button-primary-hover@2x.png` | 320×80 | 27.8 KB |
| `frames/button-primary-normal.png` | 160×40 | 9.7 KB |
| `frames/button-primary-normal.svg` | SVG | 0.5 KB |
| `frames/button-primary-normal@2x.png` | 320×80 | 28.5 KB |
| `frames/button-primary-pressed.png` | 160×40 | 9.5 KB |
| `frames/button-primary-pressed@2x.png` | 320×80 | 27.9 KB |
| `frames/button-secondary-disabled.png` | 160×40 | 8.4 KB |
| `frames/button-secondary-disabled@2x.png` | 320×80 | 26.2 KB |
| `frames/button-secondary-hover.png` | 160×40 | 9.7 KB |
| `frames/button-secondary-hover@2x.png` | 320×80 | 28.1 KB |
| `frames/button-secondary-normal.png` | 160×40 | 9.7 KB |
| `frames/button-secondary-normal.svg` | SVG | 0.5 KB |
| `frames/button-secondary-normal@2x.png` | 320×80 | 28.3 KB |
| `frames/button-secondary-pressed.png` | 160×40 | 9.3 KB |
| `frames/button-secondary-pressed@2x.png` | 320×80 | 27.9 KB |
| `frames/corner-bronze-bl.png` | 18×18 | 0.2 KB |
| `frames/corner-bronze-bl.svg` | SVG | 0.3 KB |
| `frames/corner-bronze-bl@2x.png` | 36×36 | 0.2 KB |
| `frames/corner-bronze-br.png` | 18×18 | 0.2 KB |
| `frames/corner-bronze-br.svg` | SVG | 0.3 KB |
| `frames/corner-bronze-br@2x.png` | 36×36 | 0.2 KB |
| `frames/corner-bronze-tl.png` | 18×18 | 0.2 KB |
| `frames/corner-bronze-tl.svg` | SVG | 0.3 KB |
| `frames/corner-bronze-tl@2x.png` | 36×36 | 0.2 KB |
| `frames/corner-bronze-tr.png` | 18×18 | 0.2 KB |
| `frames/corner-bronze-tr.svg` | SVG | 0.3 KB |
| `frames/corner-bronze-tr@2x.png` | 36×36 | 0.2 KB |
| `frames/corner-bronze.svg` | SVG | 0.3 KB |
| `frames/divider-medallion.png` | 44×14 | 0.6 KB |
| `frames/divider-medallion.svg` | SVG | 0.3 KB |
| `frames/divider-medallion@2x.png` | 88×28 | 1.3 KB |
| `frames/divider-thread-medallion.png` | 712×14 | 0.8 KB |
| `frames/divider-thread-medallion.svg` | SVG | 0.5 KB |
| `frames/divider-thread-medallion@2x.png` | 1424×28 | 1.9 KB |
| `frames/focus-ring-leather.png` | 176×56 | 0.9 KB |
| `frames/focus-ring-leather.svg` | SVG | 0.3 KB |
| `frames/focus-ring-leather@2x.png` | 352×112 | 2.0 KB |
| `frames/focus-ring-parchment.png` | 176×56 | 0.6 KB |
| `frames/focus-ring-parchment.svg` | SVG | 0.2 KB |
| `frames/focus-ring-parchment@2x.png` | 352×112 | 1.2 KB |
| `frames/nineslice.json` | JSON | 5.1 KB |
| `frames/panel-active-medium.png` | 384×284 | 48.0 KB |
| `frames/panel-active-medium.svg` | SVG | 2.1 KB |
| `frames/panel-active-medium@2x.png` | 768×568 | 115.8 KB |
| `frames/panel-active-wide.png` | 724×134 | 39.4 KB |
| `frames/panel-active-wide.svg` | SVG | 2.5 KB |
| `frames/panel-active-wide@2x.png` | 1448×268 | 97.6 KB |
| `frames/panel-sheet-medium.png` | 384×284 | 48.3 KB |
| `frames/panel-sheet-medium.svg` | SVG | 1.4 KB |
| `frames/panel-sheet-medium@2x.png` | 768×568 | 117.0 KB |
| `frames/panel-sheet-wide.png` | 724×134 | 39.3 KB |
| `frames/panel-sheet-wide.svg` | SVG | 1.7 KB |
| `frames/panel-sheet-wide@2x.png` | 1448×268 | 98.0 KB |
| `frames/stitch-h-tile.png` | 10×4 | 0.1 KB |
| `frames/stitch-h-tile.svg` | SVG | 0.1 KB |
| `frames/stitch-h-tile@2x.png` | 20×8 | 0.1 KB |
| `frames/stitch-v-tile.png` | 4×10 | 0.1 KB |
| `frames/stitch-v-tile.svg` | SVG | 0.1 KB |
| `frames/stitch-v-tile@2x.png` | 8×20 | 0.1 KB |
| `frames/tab-parchment.png` | 228×64 | 6.5 KB |
| `frames/tab-parchment.svg` | SVG | 0.2 KB |
| `frames/tab-parchment@2x.png` | 456×128 | 14.7 KB |
| `frames/window-frame.png` | 960×600 | 409.7 KB |
| `frames/window-frame.svg` | SVG | 0.7 KB |
| `frames/window-frame@2x.png` | 1920×1200 | 1.43 MB |

## UI kit (`assets/ui/`)

A second, larger art set for the Avalonia theme (F3a): 114 pieces in `assets/ui/<group>/` (`panels`, `buttons`, `fields`, `focus`, `popups`, `controls`, `shadows`), each as `name.png` (@1x), `name@2x.png` and, where it exists, a flat `name.svg`. Metadata is in `assets/ui/nineslice.json` (version 2). The full inventory, insets, usage rules, design corrections, verification results and limitations are in `assets/ui/README.md`.

It sits **next to** the legacy pieces in `assets/frames/` and does not replace them yet. Overlap: `panel-*` / `tab-parchment` (legacy, baked 12/10 px shadow), `button-{primary,secondary}-*` and `focus-ring-*` (same names exist in both sets, in different folders, with different insets). The kit has no baked shadows; use `shadows/shadow-soft` as a separate layer. Not wired into the app yet and not tested inside Avalonia.

License: own artwork, MIT. Third-party material baked into some PNGs: ambientCG Leather037 (leather buttons and popup) and Metal008 (scrollbar thumbs, progress fill), both CC0 1.0; the parchment grain is procedural. Generator and verification scripts are not in the repository (see `assets/ui/README.md`, section 7).

## Branding (`assets/branding/`)

| File | Purpose |
|---|---|
| `assets/branding/icon/svg/realm-portal.svg` | App icon "Tesera", full colour (≥ 48 px) |
| `assets/branding/icon/svg/realm-portal-simplified.svg` | 32 px |
| `assets/branding/icon/svg/realm-portal-micro.svg` | 16, 22, 24 px |
| `assets/branding/icon/svg/realm-portal-mono.svg`, `-mono-negative.svg`, `-mono-simplified.svg` | one-colour versions |
| `assets/branding/icon/svg/realm-portal-symbolic.svg` | 16×16 symbolic icon |
| `assets/branding/icon/png/realm-portal-{16,22,24,32,48,64,128,256,512}.png` | transparent PNGs, rendered natively per size |
| `assets/branding/icon/windows/realm-portal.ico` | 16, 24, 32, 48, 64, 128, 256 |

Linux icons live in `packaging/linux/icons/hicolor/` with the name `com.karloz7715.RealmPortal`. See `assets/branding/README.md` for the concept, grid, colours and usage rules.

License: own design, original artwork under the repository's MIT license (see `assets/branding/LICENSE-ART.md`); no third-party textures, fonts or artwork.

## 3. icons/

### icons/seals — status seals (SVG 56×56, PNG 64 and 128)

| State | SVG | PNG | Color / glyph |
|---|---|---|---|
| active | `icons/seals/seal-active.svg` | `seal-active-64.png`, `seal-active-128.png` | wax red · ring and dot (live realm) |
| ready | `icons/seals/seal-ready.svg` | `seal-ready-64.png`, `seal-ready-128.png` | forest green · check mark |
| incompatible | `icons/seals/seal-incompatible.svg` | `seal-incompatible-64.png`, `seal-incompatible-128.png` | amber · warning triangle |
| no-response | `icons/seals/seal-no-response.svg` | `seal-no-response-64.png`, `seal-no-response-128.png` | muted · cross |
| inactive | `icons/seals/seal-inactive.svg` | `seal-inactive-64.png`, `seal-inactive-128.png` | muted · ring only |

State is never conveyed by color alone: every seal carries its glyph. "Incompatible" corresponds to the seal named "Aviso" (warning) in Figma.

### icons/ui — 23 interface icons (SVG 24×24)

1.6–2 px stroke, square caps; no fill except `play` and `diamond`. `icons/ui/` in bone color `#EFE5CD` (on leather) and `icons/ui-ink/` in `#2B2118` (on parchment). File name → Figma component name (the Figma file keeps its original component names; also in `icons/ui/_names.json`):

| File | Figma | File | Figma | File | Figma |
|---|---|---|---|---|---|
| `servers.svg` | Icono/servers | `folder.svg` | Icono/folder | `gear.svg` | Icono/gear |
| `search.svg` | Icono/search | `plus.svg` | Icono/plus | `chevron-down.svg` | Icono/chev |
| `warning.svg` | Icono/warn | `info.svg` | Icono/info | `ok.svg` | Icono/check |
| `error.svg` | Icono/x | `play.svg` | Icono/play | `diamond.svg` | Icono/diamond |
| `lock.svg` | Icono/lock | `undo.svg` | Icono/undo | `minimize.svg` | Icono/min |
| `maximize.svg` | Icono/max | `close.svg` | Icono/close | `globe.svg` | Icono/globe |
| `clock.svg` | Icono/clock | `refresh.svg` | Icono/refresh | `pencil.svg` | Icono/pencil |
| `copy.svg` | Icono/copy | `list.svg` | Icono/list |  |  |

**Provenance**: drawn by hand for this project with a project script (not included in this repository); they do not come from the Figma connector or from Lucide (ISC), Feather, Heroicons or any other library, so there is no third-party attribution. To change the color, edit the `stroke`/`fill` attribute (Avalonia.Svg does not guarantee `currentColor`).

## 4. fonts/

See `fonts/FONTS.md` (OFL 1.1 license, source URLs, file types and glyph check).

## Honest limitations

- Nothing has been tested inside Avalonia; verification is visual with Pillow/cairosvg (9-slice implemented in Python with the same insets). Any rendering difference (anti-aliasing, DPI) will be discovered in the spike.
- The SVGs in `frames/` are flat (no texture, shadows or inner shadow): the effects exist only in the PNGs, because cairosvg/Avalonia.Svg do not guarantee `feGaussianBlur`/`<pattern>`. Either accept "two looks" or use the PNGs.
- The irregular parchment edge is a fixed shape: when stretched, the waviness stretches too, and the decorated zone requires ≥72 px per side. Panels wider than ~1000 px will show very soft waviness (it cannot be tiled).
- The dotted stitching of buttons and hardware stretches in the 9-slice center (it does not keep the 6 px period).
- The seals are not pixel-fitted at 16 px (the 16×16 is a reduction of the simplified design; legible as a red seal with an arch, but without detail).
- JetBrains Mono does not include the ✓ glyph (U+2713); do not use it in code UI (use "OK" or an SVG icon).
- There is no @2x variant of the interface icons or of the seals: they are vector; the seal PNGs are given at 64 and 128.
- Textures visibly repeat on very large screens if tiled without variation (parchment more than leather).
