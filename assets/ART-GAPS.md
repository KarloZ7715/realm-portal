# Realm Portal - art gaps for the designer (F3a)

Status: 2026-10-17. First written while building the component gallery
(`dotnet run --project src/RealmPortal.App -- --gallery`), then updated after the designer's UI kit (`assets/ui/`) was integrated.
Each open item says what the code does **instead** (the stand-in), so nothing is blocked.

Conventions for new files (same as the kit): PNG @1x + `@2x` (exactly double the pixels), straight alpha, English file names, an entry in
`assets/ui/nineslice.json` with `png`, `insets` (left, top, right, bottom, in @1x px), `mode`, `tile_axes` and `shadow_margin`.

## Resolved by the UI kit (`assets/ui`)

| Original gap | Kit piece | How the theme uses it |
|---|---|---|
| Thin panel for rows <= 72 px | `panel-thin-{normal,hover,selected,selected-hover}` | `ListBox.cards` rows and `ContentControl.card.thin` (`Theme/Controls/Card.axaml`). |
| Tileable / wider centres | `panel-tile-*`, `button-*`, `split-*` | `NineSlice.TileX/TileY`: corners fixed, edge and centre cells tiled with phase 0 at the cell's top-left. Native size reproduces the PNG exactly (`SeamTests.TheNativeSizeReproducesThePieceExactly`). |
| No-shadow variants | all kit plates, `tab-parchment-flat`, `shadow-soft` | Plates have no shadow; `shadow-soft` is a separate layer under cards and the selected tab (`NineSlice.ShadowMargin = 12` places it). |
| Joined split-button plate | `split-{primary,secondary}-*` | `SplitPlate` draws the plate and re-draws the hovered/pressed segment from the state plate, clipped to it (`Theme/Controls/SplitButton.axaml`, on Avalonia's `SplitButton`). |
| Card hover | `panel-thin-hover`, `panel-tile-hover` (+ `-selected-hover`) | Replaces the 1 px lift. |
| Ghost button plate | `ghost-parchment-*`, `ghost-leather-{hover,pressed}` | `Button.ghost` / `.ghost.parchment`. |
| Compact (56 px) tab | `tab-parchment-flat` (inset 18) | `ListBox.nav.compact`; renders cleanly at 56 px wide. |
| Fields, checkbox, radio, switch, scroll bar, progress | `fields/*`, `controls/*` | `Inputs.axaml`, `ScrollBar.axaml`, `ProgressBar.axaml`; checkbox/radio/switch via `StateArt`. |

## Open items

Priority: **P1** = visible in the F3b screens, **P2** = nice to have, **P3** = only if cheap.

| # | Gap | Priority | Stand-in today | Notes |
|---|---|---|---|---|
| 1 | **Menu / dropdown / tooltip theming** | P1 | Not themed yet (Simple look). The kit has `popup-leather` and `popup-parchment`, but no menu-item hover or ComboBox art. | Needed for the split-button menu ("only change"), language and client selectors, context menus. Code work is mine once the designer confirms: flat wood hover row inside the popup plate, or a textured one. |
| 2 | **Design sign-off for kit pieces that are not in Figma** | P1 | Used as delivered. | Card hover, `ghost-leather-{hover,pressed}` (Figma shows a leather ghost with no plate, only a lighter label), scroll bar, progress stripes, checkbox/radio/switch hover and disabled, popup plates. |
| 3 | **Pressed art for checkbox, radio and switch** | P3 | The hover art is used while pressed. | |
| 4 | **Parchment progress bar** | P3 | Not provided; the theme has the leather bar only. | Only if a progress bar appears on parchment. |
| 5 | **Nav item hover** | P3 | Flat wood/700 rectangle with 4 px radius (matches the mockup). | Only needed if a textured hover is wanted. |
| 6 | **Scroll bar grip on very short thumbs** | P3 | The grip is always drawn (the kit asks to hide it under 28 px). | Code change, not art: needs a length-based visibility rule. |
| 7 | **Retire the legacy frames** | P3 | `assets/frames/` is no longer bundled into the app (the kit replaces its panels, buttons, tab and focus rings). | Confirm that `assets/frames` can be removed or kept only as source, so the repo does not carry two sets. |

## Not gaps (checked, no art needed)

- Seals: the 64/128 px PNGs are enough (128 px is used above 64 device pixels, so 48 DIP at 150 % and 200 % is crisp).
- `window-frame` (960x600 with the title bar baked in) is **not used**: the app keeps the system title bar. A custom frame would be a separate decision
  (window drag, resize and snapping on Windows).
- The dashed "stitch" borders on banners can use `stitch-h-tile` / `stitch-v-tile` (already in the pack).

## Findings about the kit

- Every piece (checked in the theme, plus all pieces on disk) has a `@2x` file with exactly double the pixels.
- Seam test: panels, thin panels, buttons, split plates and the flat tab render without gaps at 1.0, 1.25, 1.5, 1.75 and 2.0 render scale, including widths off the
  `160 + 6k` / `200 + 6k` grid (`tests/RealmPortal.App.Tests/SeamTests.cs`).
- Off-grid button widths can shorten one stitch dash near the right cap, as `assets/ui/ASSETS.md` says; the gallery shows 100, 166, 232, 251 and 400 px.
- The selected sidebar tab now carries the wax-red edge at the window side, as in the mockup (rec-12b).
