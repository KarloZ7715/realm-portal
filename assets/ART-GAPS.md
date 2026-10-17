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
| 1 | **Menu / dropdown / tooltip art** | P1 | Themed with the closest kit pieces (F3b M1): `popup-leather` / `popup-parchment` plates, FLAT hover per the designer's F4 decision (14 % solid token fill, bronze over leather and wax red over parchment, plus a 3 px wax-red bar on the left; the selected ComboBox item gets a stronger fill and no bar), no shadow layer. | Real art needed for menu item hover, separators, ComboBox list and tooltip, plus a popup shadow. See "Pieces to validate" below. |
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

## Pieces to validate with the designer (F3b)

Everything below is built and visible in the F3b screens, but has **no mockup** (or only a hint of one). The list is what the product owner should look at
when checking the build; screenshots at 1.0, 1.5 and 2.0 come from the UI tests (`RP_SCREENSHOT_DIR`).

| # | Piece | Where it shows | What was done | Question |
|---|---|---|---|---|
| 1 | **Card hover** (`panel-tile-hover`) | server cards | Kit hover art on pointer over. Cards are not clickable as a whole, only their buttons, so hover may mislead. | Keep the hover, or no hover on cards? |
| 2 | **Ghost button on leather** (`ghost-leather-hover/pressed`) | none in F3b screens now; "Limpiar búsqueda" and "Cancelar" are plain text links | The undo notice uses the secondary button like the mockup. Text links have no hover plate, only a brighter label and a thin bronze keyboard outline. | Should text links get the ghost plate on hover? |
| 3 | **Scroll bar** | server list and client list when they overflow | Kit scroll bar. The mockups never show one (they crop the list). | Position and width OK next to the cards? |
| 4 | **Progress bar** with running stripes | switching and opening banners | Kit track and fill; the stripes are on while busy (the mockups show a plain fill). | Keep the stripes? |
| 5 | **Hover / disabled** of buttons, cards, split button, checkbox | all screens | Kit states. The disabled split button on cards while busy is the dark disabled plate as in mockups 04 and 06. | Disabled "Reintentar" is dimmer than in mockup 06b. |
| 6 | **Menu** (split button "Solo cambiar") | card menu | `popup-leather` plate, flat hover (fill + 3 px wax bar), no shadow layer. | Needs real menu art: plate, item hover, separator, shadow. |
| 7 | **ComboBox** (client choice) | add server | Input plate with chevron; list in `popup-parchment`, flat hover (fill + 3 px wax bar) and a stronger fill for the selected item. | Needs real dropdown art. |
| 8 | **Tooltip** | sidebar items, language toggle, search clear | `popup-leather` plate, bone text. | Needs real tooltip art. |
| 9 | **Banner, steps, toast** | switching, opening, running, undo | Drawn in code from the mockups (dashed outline, 4 px bar, round markers, countdown line); only the toast uses kit art (`popup-leather` + `shadow-soft`). | Dash pattern and sizes against the mockups. |
| 10 | **Status bar, sidebar edge, sheet titles** | all screens | Drawn in code (bronze-dark rules, dashed edge). | Colours of the rules. |
| 11 | **Wide sidebar** (960 px and up) | any screen in a wide window | Labels next to the icons, non-affiliation note at the bottom. | No mockup at this size. |
| 12 | **Origin and result badges** | clients ("LUTRIS", "WINE", "STEAM", "MANUAL"), add server result | The inactive badge for origins; ready / no-response / inactive for the test result. The badge border now uses the status colour as in the mockups. | Is "Sin respuesta" (error glyph) right for a failed test? |

### States marked "Definido aquí" in UX-DESIGN.md 2b

Filtered list, no results, switching, opening the game and game running are built as designed. They still wait for the product owner's approval; see
`docs/ui-screens.md` for the assumptions that go with them.

### Seals

The seals in the repository are now the designer's v2 set (ring and dot for active, ring only for inactive). The old portal-arch version of those two is gone.
