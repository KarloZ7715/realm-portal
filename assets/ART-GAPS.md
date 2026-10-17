# Realm Portal - art gaps for the designer (F3a)

Status: 2026-10-17, written while building the component gallery (`dotnet run --project src/RealmPortal.App -- --gallery`).
Everything below works today with the current pack; each item says what the code does **instead** (the stand-in), so
nothing is blocked. When new art lands, the swap is a one-line change per item (the theme files are named in each entry).

Conventions for new files (same as the current pack): PNG @1x + `@2x` (exactly double the pixels), straight alpha, English file
names, entry in `assets/frames/nineslice.json` with `png`, `insets` (left, top, right, bottom, in @1x px), `shadow_margin` and
`body`. Say whether a shadow is baked in: the code compensates for it (`NineSlice.ShadowMargin`).

Priority: **P1** = visible in the F3b screens, **P2** = nice to have, **P3** = only if cheap.

| # | Gap | Priority | Stand-in today | Where it is used / proposed spec |
|---|---|---|---|---|
| 1 | **Thin panel** for rows and popovers | P1 | The 36 px inset panel, which works down to a body of 48 px but leaves almost no room for content (the inner rule line and the torn edge take about 24 px of the body on every side). | Compact client rows (<= 72 px), toasts, tooltips, popovers, the "Cambio deshecho" bar. Spec: `panel-sheet-thin` + `panel-active-thin`, inset about 18-20, baked shadow 8 (or none, see #3), source canvas about 360x72. Theme: `Theme/Controls/Card.axaml`. |
| 2 | **Tileable / wider centres** for panels, buttons and tabs | P1 | Plain 9-slice stretch. | Stretch limits seen in the gallery: panels look fine up to about 1.5x the native width (724 -> ~1000 px; beyond that the torn-edge wave and the paper grain smear). Buttons are fine up to about 240 px wide; at 400 px the stitching dashes and the leather grain stretch visibly (`Stretched far` in the gallery). Ask: either centre strips that tile (so the code tiles instead of stretching) or extra widths (panel 900 and 1200; button 240 and 320). Tab: a 56 px wide **compact tab** (icon-only sidebar of the 720x480 layout); today the 228 px art is squeezed. |
| 3 | **No-shadow variants** of the panel, the tab and the focus ring | P2 | `ShadowMargin` makes the control draw the art outside its bounds; works, but a panel inside a panel (or on parchment) gets a double shadow, and screens must leave 12 px around every card. | `panel-*-flat`, `tab-parchment-flat`; or a separate shadow layer (`shadow-panel`) so the code decides. |
| 4 | **Joined split-button plate** (action + chevron) | P1 | Two normal plates side by side, 4 px apart (the mockup shows one plate with a seam). | `button-split-{primary,secondary}-{normal,hover,pressed,disabled}`, about 200x40, insets 14/14, with a fixed 40 px chevron section at the right and stitching/rivets on both parts. The chevron half should be able to hover/press on its own: either provide the two halves as separate frames (`-main`, `-drop`) with a flat joined edge, or tell us the hit-area split. Theme: `Theme/Controls/Button.axaml` + a future `SplitButton` theme. |
| 5 | **Card hover** (and pressed) art | P2 | The card lifts 1 px (`RenderTransform`), nothing else. | `panel-sheet-wide-hover` (and `panel-active-*-hover`); a slightly lighter paper or a raised bronze edge would do. Same insets and shadow as the normal panel so the code only swaps the name. |
| 6 | **Ghost button plate** (text button) | P2 | A flat rounded tint rectangle (wood/700 on leather, 20 % black on parchment) for hover and pressed, nothing for normal. | `button-ghost-{hover,pressed}` for leather and `button-ghost-parchment-{hover,pressed}`, inset 14, same 160x40 canvas. Disabled is just dimmed text. |
| 7 | **Nav item hover** | P3 | Flat wood/700 rectangle with 4 px radius (matches the mockup). | Only needed if the designer wants a textured hover; otherwise no gap. |
| 8 | **Popup / dropdown / menu plate** | P1 | Not themed yet (Simple look). | Split-button menu ("Solo cambiar"), language/client selectors, context menus. Likely the thin panel of #1 plus a menu-item hover state. Please confirm which art to use. |
| 9 | **Scroll bar** | P3 | Simple theme default. | Thin bronze thumb on a leather track; vector is enough (code-drawn). |
| 10 | **Progress bar, stepper, alerts/banners** | P3 (F3b) | Not built in F3a. | Code-drawn in the mockup (rec-12b); no art needed unless the designer wants stitched borders. |

## Not gaps (checked, no art needed)

- Text fields, check box, radio button and switch are drawn in code and match rec-12b. The mockup's dashed "stitch" borders on
  banners can use `stitch-h-tile` / `stitch-v-tile` (already in the pack).
- Seals: the 64/128 px PNGs are enough (128 px is used above 64 device pixels, so 48 DIP at 150 % and 200 % is crisp).
- `window-frame` (960x600 with title bar baked in) is **not used**: the app keeps the system title bar. If the designer wants a custom
  frame later, that is a separate decision (window drag, resize and snapping on Windows).

## Findings about the current art

- Corner radius and stitching of the primary/secondary buttons survive @1x, 1.25x, 1.5x, 1.75x and 2x without seams (automated seam test in
  `tests/RealmPortal.App.Tests/SeamTests.cs`).
- Focus rings (`focus-ring-leather/parchment`, 176x56, inset 14, 8 px overflow) stretch well to wide cards and tabs.
- The parchment texture of the panels shows its large stains when a panel is wider than about 1000 px (see #2).
