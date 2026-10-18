# Realm Portal UI kit: art for the Avalonia theme

Own artwork, MIT licensed (see "Licence"). 130 pieces in `assets/ui/<group>/`, each as `name.png` (@1x) and `name@2x.png` (@2x), plus a flat vector `name.svg` where one exists. Machine-readable metadata: `assets/ui/nineslice.json`. Everything is RGBA with straight alpha, rendered natively at each scale (nothing is resampled, so there are no halos).

This kit answers the art requests from the Avalonia style spike (F3a). It sits **next to** the legacy frames in `assets/frames/` and does not remove them: the legacy panels (with baked shadow) and 160x40 buttons still work for the sizes the spike found acceptable.

Colours and states follow the "Parchment and bronze" design (tokens in `src/RealmPortal.App/Theme/Tokens.axaml`); state colours were read from the Figma component library, not guessed.

## 1. Rules that apply to every piece

* **Insets** are `[left, top, right, bottom]` in @1x px. For @2x multiply by 2.
* **Modes** (field `mode` in `nineslice.json`):
  * `stretch`: classic 9-slice.
  * `tile`: corners fixed; the four edge cells and the centre cell are **tiled, never stretched**. `tile_axes: "x"` means tile horizontally only (buttons: fixed height); no `tile_axes` means both axes (panels).
* **No outer shadow is baked into any piece of this kit.** The legacy buttons and window frame never had one (checked: the body reaches the canvas edge); the legacy panels/tab did (12/10 px). Shadows are a separate layer: `shadows/shadow-soft` (or Avalonia `BoxShadow="0 3 7 0 #80000000"`). So there are **no negative margins** to compensate.
* **Borders are drawn inside the bounds** (fields, checkboxes, switches...): going from a 1 px to a 2 px (focus/error) border does not move the layout.
* Focus rings are separate pieces (needs `ClipToBounds="False"` on the host, as the spike found).
* Pick @2x when `RenderScaling >= 1.5`, with high-quality interpolation (as in the spike).
* The label, icon and chevron are **never baked into the art**.

### Reference algorithm (what the `tile` mode means)

```
// src = the PNG for the current scale (S = 1 or 2), insets (l,t,r,b) already multiplied by S
// dest = target rect in device px
for each of the 9 cells:
  corners:                 draw once, 1:1
  edge/centre cells:       mode == stretch  -> DrawImage(src cell -> dest cell)
                           mode == tile     -> repeat the source cell with its top-left at the dest cell's
                                               top-left (phase 0), clipped to the dest cell,
                                               only along tile_axes; the other axis stretches
```

`NineSlice.cs` from the spike only needs a `TileMode` property and a clipped `DrawImage` loop (or an `ImageBrush { TileMode = Tile, DestinationRect = cell }` per cell). A Python implementation of exactly this rule was used for all test compositions (not Avalonia); it is not included in the repository (see section 7).

## 2. Inventory

Sizes are @1x (the @2x file has twice the pixels). "min" = smallest destination size that keeps the corners intact.

### 2.1 Parchment panels (`panels/`)

| Piece | Canvas | Insets | Mode | Use |
|---|---|---|---|---|
| `panel-thin-normal` | 112x64 | 20, 20, 20, 20 | stretch, min 40x40 | **thin panel for rows <= 72 px**, list items, popovers (request 1). Native centre cell is 72x24; fine up to ~1.5x native (168x96), beyond that use `panel-tile-*` |
| `panel-thin-hover` | 112x64 | 20, 20, 20, 20 | stretch | hover: fill 40 % towards `#E9DDBF`, bronze filet `#A8743F` (request 5) |
| `panel-thin-selected` | 112x64 | 20, 20, 20, 20 | stretch | selected: baked bronze corner brackets |
| `panel-thin-selected-hover` | 112x64 | 20, 20, 20, 20 | stretch | selected + hover |
| `panel-tile-normal` / `-hover` / `-selected` / `-selected-hover` | 296x296 | 20, 20, 20, 20 | **tile (x and y)**, min 40x40 | **tileable parchment for any size** (request 2). Atlas = 20 px corners + 256x256 edge and centre cells. Use for the big cards and dialogs instead of stretching |
| `tab-parchment-flat` | 208x44 | 18, 18, 18, 18 | stretch | selected sidebar tab **without** the 10 px shadow margin: x = 0 is the window edge (request 3) |
| `popup-parchment` | 96x64 | 16, 16, 16, 16 | stretch | popover / menu plate, straight edges |

The three insets of the legacy panel (36) become **20** here; corner brackets (18 px) fit inside.

How to tile a card: draw `shadow-soft` (dest = card rect inflated by 12 px), then the `panel-tile-*` nine-slice over the card rect. `hover` and `selected` are separate pieces, swap them with the card state.

Texture note: the kit's parchment grain is **procedural** (own work, seamless by construction, mean/contrast matched to the CC0 Paper003 used by the legacy panels). The legacy panels have large recognisable stains; those repeat visibly when tiled, so they were deliberately left out. Legacy and kit panels can sit side by side; the kit looks slightly calmer.

### 2.2 Shadow (`shadows/`)

| Piece | Canvas | Insets | Mode | Use |
|---|---|---|---|---|
| `shadow-soft` | 48x48 | 18, 18, 18, 18 | stretch | black shadow only (0 3 / blur 3.5 / 50 %), 12 px margin; draw under any flat plate with dest = plate rect +- 12 px |

### 2.3 Leather buttons (`buttons/`)

| Piece | Canvas | Insets | Mode | Use |
|---|---|---|---|---|
| `button-{primary,secondary}-{normal,hover,pressed,disabled}` (8) | 160x40 | 17, 14, 17, 14 | **tile x**, centre cell 126 px, min 34x28 | leather button with tileable centre (request 2). Native 160x40 = Figma size. Height policy: 36-44 px (rows 14..26 stretch) |
| `split-{primary,secondary}-{normal,hover,pressed,disabled}` (8) | 200x44 | 16, 14, 58, 14 | **tile x**, centre cell 126 px, min 74x28 | **one joined plate** for Play + chevron (request 4). Native 200x44 = Figma size: action part + 44 px menu part overlapping by 1 px (shared seam) |
| `ghost-parchment-{normal,hover,pressed,disabled}` (4) | 64x40 | 14, 14, 14, 14 | stretch | ghost button on parchment (request 6): outline `#5A3D22`; hover `#C2AE84` @ 50 %; pressed @ 90 % (values from the Figma "Parchment text" component) |
| `ghost-leather-{hover,pressed}` (2) | 64x40 | 14, 14, 14, 14 | stretch | ghost button on leather. **Optional extension** (revised): in Figma this button has no plate (only the label goes `#EFE5CD` -> `#F6EBD2` hover -> `#C4B494` pressed). Hover = raised wood plate with a `#8A6C4A` outline; pressed = **sunken** plate (darker than the window, inner shadow) with the same outline, so the two are no longer alike. Normal and disabled have no art |

Split plate, how to use:
* Menu segment width is fixed at 44 px; the action segment grows by tiling the centre cell (the Figma plate is 157 + 44 - 1 px).
* **Per-segment states:** draw the `normal` plate, then draw the `hover`/`pressed` plate **clipped to the hovered segment** only. Action segment = `x < W - 43.5`; menu segment = `x >= W - 43.5` (dest px). All state plates share the same geometry, so the seam stays continuous (checked in the design workspace test compositions).
* **Focus:** use `focus/focus-ring-leather` around the whole plate (216x60 for the 200x44 plate) or around the menu segment alone (44x44 element).
* Disabled: use the `disabled` plate for both segments (no stitch, no rivets).

Tiling accuracy: for widths `W = 160 + 6k` (plain) or `200 + 6k` (split) the tiled result matches a direct render except for a barely visible 15/255 grain phase step at the right cap. For other widths one dash of the stitch near the right cap can be shortened or lengthened (< 6 px), because the stitch period is 6 px. If that bothers you, snap widths to those values.

### 2.4 Focus rings (`focus/`)

| Piece | Canvas | Insets | Use |
|---|---|---|---|
| `focus-ring-leather`, `focus-ring-parchment` | 64x56 | 14, 14, 14, 14 | buttons, fields, cards, split segments. Element centred with 8 px margin (ring sits 3 px outside the element). Double ring on leather, teal on parchment |
| `focus-ring-control-leather`, `-parchment` | 32x32 | 12, 12, 12, 12 | 22 px checkbox/radio (5 px margin -> 32x32); stretch for switches (56x34) and search fields (206x46), as in Figma |

### 2.5 Fields and popups (`fields/`, `popups/`)

| Piece | Canvas | Insets | Use |
|---|---|---|---|
| `input-leather-{normal,hover,focus,error,disabled}` (5) | 64x40 | 14, 14, 14, 14 | text field on leather. Border 1 px `#A8743F` (hover `#D2A268`); focus 2 px `#F0BC82`; error 2 px `#E58A7C`; disabled 1 px `#5A3D22` |
| `input-parchment-{normal,hover,focus,error,disabled}` (5) | 64x40 | 14, 14, 14, 14 | text and search field on parchment. Border 1 px `#5A3D22` (hover `#895E33`, 4.19:1 on `#E9DDBF`; the earlier `#A8743F` gave 2.97:1); focus 2 px `#0F5561`; error 2 px `#8A2A25` |
| `popup-leather` | 96x64 | 16, 16, 16, 16 | dropdown / context menu / tooltip on leather, bronze border + faint stitch. Put `shadow-soft` under it |
| `popup-parchment` | 96x64 | 16, 16, 16, 16 | see 2.1 |

**Menu-item / ComboBox-item / tooltip hover (design decision, flat, no art):** a solid fill with no texture plus a 3 px bar on the item's left edge as a second cue (never colour alone).
* On `popup-parchment`: fill `#D1BB92` (bronze `#A8743F` at 14 % over `#D8C7A0`), bar wax `#7A2723`. Ink `#2B2118` on the fill = 8.42:1, ink-soft `#54442F` = 5.00:1, bar on fill = 5.24:1.
* On `popup-leather`: fill `#372616` (bronze at 14 % over the plate), bar `wax/light` `#E58A7C` (6.9:1 on the plate; plain wax is only 1.78:1 on leather). Bone `#EFE5CD` on the fill = 11.8:1.
* Disabled item: text `#6B5842` (4.06:1 on `#D8C7A0`, exempt) and a sub-line that says why. Do not use `Opacity=0.5` on the whole item.

### 2.6 Controls (`controls/`)

| Piece | Canvas | Insets | Use |
|---|---|---|---|
| `scrollbar-v-track-{leather,parchment}` | 12x48 | 5, 8, 5, 8 | vertical track, 12 px wide, stretch vertically |
| `scrollbar-v-thumb-{leather,parchment}-{normal,hover,pressed}` (6) | 8x40 | 4, 8, 4, 8 | thumb, 8 px wide inside the 12 px track (2 px margin), min height 16 |
| `scrollbar-h-*` | rotated | see json | horizontal versions: the vertical art rotated 90 deg counter-clockwise |
| `scrollbar-grip-{leather,parchment}` | 8x10 | none | 3-line grip centred on the thumb (hide under 28 px) |
| `progress-track` | 48x12 | 6, 6, 6, 6 | 12 px track; stretch horizontally |
| `progress-fill` | 32x8 | 4, 4, 4, 4 | bronze fill, 8 px high, placed at (2,2) in the track; width = value x (track - 4), min 8 |
| `progress-track-parchment` | 48x12 | 6, 6, 6, 6 | 12 px track for parchment surfaces (border `#5A3D22`, 5.9:1 on `#D8C7A0`); stretch horizontally |
| `progress-fill-parchment` | 32x8 | 4, 4, 4, 4 | dark-bronze fill (`#6E4B29`, 4.2:1 against the track), 8 px high, placed at (2,2); same width rule as `progress-fill` |
| `progress-stripes-tile` | 12x8 | none | **indeterminate / "switching" overlay**: tile it horizontally over the fill and animate its x offset 0 -> 12 px (period 12, loops seamlessly) |
| `checkbox-{leather,parchment}-{unchecked,checked,indeterminate}-{normal,hover,pressed,disabled}` (24) | 22x22 | none (fixed) | 22x22 |
| `radio-{leather,parchment}-{off,on}-{normal,hover,pressed,disabled}` (16) | 22x22 | none (fixed) | 22x22, dot 10 px |
| `switch-track-{leather,parchment}-{off,on}-{normal,hover,pressed,disabled}` (16) | 46x24 | 12, 0, 12, 0 | 46x24, 3-slice horizontal |
| `switch-thumb-{on,off-leather,off-parchment}` (3) | 16x16 | none | 16 px; off x = 4, on x = 26 (inside a 46 px track). Disabled: 50 % opacity |

**Pressed state of checkbox, radio and switch track** now has art on both surfaces: a *sunken* look (darker fill + inner shadow; checked = `wax/pressed` `#5A1C19`; leather off = `#0C0705`; parchment off = `#C2AE84`), same border as normal. The thumb art is unchanged (there is no pressed thumb; widening it by ~2 px while pressed is an optional runtime effect). **Parchment hover** uses a 2 px `#5A3D22` border (normal is 1.5 px) and a `#F6EBD2` fill instead of the earlier `#A8743F` border, which was only 2.97:1 against `#E9DDBF` and weaker than the normal state (7.3:1). Progress on parchment: use `progress-track-parchment` + `progress-fill-parchment`; `progress-stripes-tile` works on both (on the dark parchment fill the light stripes are subtler).

## 3. Design corrections for the Avalonia theme (minor differences listed by the architect)

* **Selected sidebar tab label:** colour `#2B2118` (ink), **Alegreya Medium** 16 px (family name "Alegreya Medium"). The unselected labels are `#EFE5CD`, same font. (Read from the Figma "Sidebar item" component.)
* **Icon stroke thickness.** In Figma each icon instance has an *absolute* stroke weight per size, and it is heavier than the 24-grid SVGs would give when scaled down:

| Icon box (px) | Stroke (px) | Same stroke in 24-grid units (`stroke x 24 / box`) | Instances found in the Figma library |
|---|---|---|---|
| 14 | 1.8 | 3.1 | 12 ("Icono") + 8 (help icon) |
| 16 | 2.0 (warning icon: 1.8) | 3.0 (2.7) | 18 ("Icono") + 4 (warning icon) |
| 18 | 1.8 (2.0 for close and minimise) | 2.4 (2.7) | search, maximise, minimise, close |
| 20 | 1.6 | 1.9 | 16 ("Icono") + 6 (servers icon) |
| 22 | 1.8 | 2.0 | undo |
| 24 | 1.8 | 1.8 | 3 ("Icono") |

  Measured with a read-only survey of every `Icono*` instance in the component library (it also found 20+ icon instances without a stroked vector, e.g. the filled play triangle and diamond). In Avalonia give the `Path` the 24-grid geometry, scale it to the box (`Stretch="None"` + a scale of `box/24`) and set `StrokeThickness = stroke_px x 24 / box_px`, or use `Stretch="Uniform"` on a box whose size is the glyph extent and the px stroke from the table.
* Checkbox tick stroke: 2.6 px, round caps/joins.

## 4. Verification (report from the design workspace; the check scripts and images are not in this repository, see section 7)

```
1) consistency (RGBA, @2x == 2 x @1x, json png size, insets fit): 130 pieces, 0 failures
2) halo test (edge-pixel colour vs palette of solid pixels, composited on magenta/white/black): 260 PNGs, 0 above 3/255; worst 0.55/255 (progress-fill @1x)
2b) no-baked-shadow check: for all panels, tabs, buttons, split plates, popups and inputs the solid body (alpha >= 200) reaches within 3 px of the canvas edge on every side (a baked shadow needs an 8-12 px margin)
3) panel-tile-normal @1x seam/interior gradient ratio: top-edge 1.03, left-edge 0.79, centre-x 1.02, centre-y 0.95 (1.0 = invisible seam)
   edge SILHOUETTE (alpha) seam ratio @1x: top 0.99, left 0.86
3) panel-tile-normal @2x seam/interior gradient ratio: top-edge 1.07, left-edge 1.08, centre-x 1.04, centre-y 0.97 (1.0 = invisible seam)
   edge SILHOUETTE (alpha) seam ratio @2x: top 1.12, left 0.94
3) panel-tile-selected @2x seam/interior gradient ratio: top-edge 1.07, left-edge 1.08, centre-x 1.04, centre-y 0.97 (1.0 = invisible seam)
3b) leather button: tiled strip vs directly rendered plate at widths 160/166/232/286/412 (= 160 + 6k), @1x/@2x, 3 states: max abs diff = 15/255
3c) split plate: tiled vs direct render at widths 200/206/326/452 (= 200 + 6k): max abs diff = 16/255
3d) leather button at widths 170/200/251 (not 160+6k): at most 48 pixels differ by > 24/255 (the shortened dash at the right cap)
4) SVG: 117 files parse as XML and render with cairosvg at the PNG size; mean |alpha| difference vs PNG silhouette: worst 0.050 (progress-stripes-tile), median 0.000
```

Visual checks (not in the repository): `test-composition-1x.png` and `-2x.png` (everything composed with the reference algorithm on leather, incl. thin rows of 44/56/64/72 px, a 960x120 tiled card, split plates with per-segment hover/pressed, focus rings), `halo-check-magenta-white-black.png` (every piece raw on magenta / white / black).

What was **not** verified: rendering inside Avalonia itself (no Avalonia run was done for this kit; the compositions use the Python reference implementation of the rules above), Windows font/AA, 1.5x scaling.

## 5. Limitations

* Kit panels use the procedural grain, not Paper003 stains (see 2.1). No stains/age bands in the kit panels.
* `panel-tile-*` is PNG only: a tileable torn edge cannot be a flat SVG. `panel-thin-*` has a flat SVG (same silhouette, no texture, no inner shadow).
* Flat SVGs of textured pieces carry colour, border, stitch and silhouette but not the texture or inner shadows. They render with cairosvg; they were not rendered in Avalonia.
* Horizontal scrollbar art is the vertical art rotated (light comes from the left instead of the top).
* Hover for cards, ghost-on-leather plates, scrollbar, progress and its stripes, checkbox/radio/switch hover, pressed and disabled, progress on parchment, and the popup plates are **new art that is not drawn in the Figma mockups** (only their normal/focus/etc. colours come from Figma). They follow the same tokens; review them before treating them as design-approved.
* Torn-edge profile is periodic every 256 px; with the 256 px centre cell, a very large panel can show the same edge ripple every 256 px (not noticeable at the sizes in the test composition).
* Button stitch period is 6 px (see 2.3 for exact widths).
* Menu/combobox chevrons, labels and icons are not part of the art.

## 6. Licence

All SVG/PNG in `assets/ui/` are original work for this project, **MIT licensed** (same as the repository; see `assets/branding/LICENSE-ART.md`). Third-party material baked into some PNGs: ambientCG **Leather037** (leather buttons, leather popup) and **Metal008** (bronze thumbs and progress fill, 25 %), both **CC0 1.0** (no attribution required; credited in `THIRD_PARTY_LICENSES.md`). Parchment grain is procedural (own work). No Blizzard assets, no fonts included.

## 7. Regenerating

The generator (`build_all.py` and helpers), the verification scripts and the verification images live in the design workspace and are **not** part of this repository: the scripts depend on absolute paths and on a shared helper module outside the repo, so they cannot be reproduced from a clone. The PNG/SVG pieces and `nineslice.json` in this folder are the source of truth. Repository-side sanity check: `nineslice.json` is valid JSON and every piece it lists has its `.png`, `@2x.png` and (where `svg` is true) `.svg` file on disk.
