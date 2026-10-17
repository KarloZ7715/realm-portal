# Realm Portal — Branding

Not affiliated with or endorsed by Blizzard Entertainment.
Trademark clearance pending (USPTO/EUIPO/WIPO, classes 9 and 42).

## The mark: the Tesera

The icon is a **tessera hospitalis**: in the ancient Mediterranean world, two hosts broke a coin or token in two and each kept one half. When the halves fit together again, the bearers recognised each other and the bond of hospitality was honoured.

For Realm Portal the idea becomes the product's promise:

- **two bronze halves = two realms**; switching realm is making the halves fit again;
- **a red sealing-wax butterfly key** bridges the crack: the piece used to repair and join broken wood or stone. It stands for the pact between realms and for trust;
- **bronze coin, leather and wax** keep the "Parchment and bronze" direction of the app (`DESIGN.md`).

It deliberately contains no dragons, crowns, swords, wings, shields, gold glare or letter "W", and nothing taken from Blizzard or any other brand. All artwork is original and MIT-licensed (see `LICENSE-ART.md`).

The key can read as a bow tie at a glance. That is a known risk: at 16–24 px the key is replaced by a plain wax dot (micro variant), and in text we call it a *butterfly key* (dovetail key).

## Files

| File | Use |
|---|---|
| `icon/svg/realm-portal.svg` | Master, full colour. Use at 48 px and above. |
| `icon/svg/realm-portal-simplified.svg` | 32 px. Flat colours, one-piece key, no rim details. |
| `icon/svg/realm-portal-micro.svg` | 16, 22 and 24 px. Split coin with a wax dot. |
| `icon/svg/realm-portal-mono.svg` / `-mono-negative.svg` | One-colour versions (ink `#1A110C`, negative `#EFE5CD`) for print, stamps, embossing and single-colour UI. Use at 48 px and above. |
| `icon/svg/realm-portal-mono-simplified.svg` | One-colour version for 24–32 px. |
| `icon/svg/realm-portal-symbolic.svg` | 16×16 single-colour symbolic icon (fill `#2e3436`, recolourable by the desktop). |
| `icon/png/realm-portal-<N>.png` | 16, 22, 24, 32, 48, 64, 128, 256, 512; transparent; each size rendered natively (not resized). |
| `icon/windows/realm-portal.ico` | 16, 24, 32, 48, 64, 128, 256 (embedded PNG). |

Size policy (used for every PNG and the .ico): **≤ 24 px micro · 32 px simplified · ≥ 48 px full.**

## Construction

- Canvas 128 × 128 u, 8 u grid (1 px at 16 px), centre (64, 64). The drawing is designed on a disc of radius 48 u and enlarged ×1.07 about the centre by the wrapper group in each SVG, so the disc ends up ≈ 103 u wide (margins ≈ 12.6 u) and the halves, pushed apart, stay inside the 8–120 safe area.
- The coin is cut by a three-segment crack: `(64,16) → (59,42) → (69,74) → (63,112)` in the full mark. The halves move apart ±3.4 u (±4.2 u in the simplified and micro marks, so the crack survives at 16 px).
- Full mark: inset ring r 38.5 and dotted ring r 44 (visible from 48 px), outer left edge line for contrast on light backgrounds, key 48 × 38 u (waist 14 u) in sealing wax with a leather outline of 2.6 u and two bronze rivets.
- Simplified mark: key 52 × 44 u in `#5A1C19`, no outline, no rings.
- Micro mark: wax dot of radius 14.5 u. Symbolic: halves with a hole of radius 13 u and a crack of 10 u.
- Heraldic contrast rule: metal (bronze) on dark; wax (a colour) on metal; never wax directly on leather without a bronze or outline separation.

## Colours

| Role | Hex |
|---|---|
| Bronze | `#A8743F` |
| Light bronze | `#D2A268` |
| Dark bronze (ring line) | `#5A3D22` |
| Wax (sealing wax) | `#7A2723` |
| Wax, dark (small sizes) | `#5A1C19` |
| Leather 900 | `#1A110C` |
| Parchment light | `#E9DDBF` |
| Bone (negative ink) | `#EFE5CD` |
| Full-mark gradients | left `#E2B77F → #C28F55`, right `#B9824A → #8F602F` |
| Small marks (flat) | left `#C28F55`, right `#A06E3B` |

Measured contrast (WCAG): small-mark left half `#C28F55` vs dark bar `#1C1C20` 5.95:1, vs light bar `#EEEEF2` 2.47:1; right half `#A06E3B` is darker still; wax dot `#5A1C19` vs left half 4.55:1.

## Usage rules

- **Minimum size:** 16 px (micro variant). Never use the full mark below 48 px.
- **Clear space:** at least 1/8 of the icon width on every side (the PNGs already include ≈ 12 % of transparent margin; do not crop it).
- **Backgrounds:** works on dark (leather, dark taskbars) and on light (parchment, white, light taskbars). On mid-tone bronze or brown backgrounds, put the icon on a dark or parchment tile.
- **One colour:** use the mono or negative file, never a tinted copy of the colour icon.
- **Do not:** recolour the wax or bronze, add glow, drop shadows or gold gradients, rotate or stretch the mark, close the crack, add a letter or a crown, place it on a busy photo, or combine it with another brand's logo or with the Blizzard name.
- **Status seals** (`assets/icons/seals/`) are a secondary visual language. They must not reuse the coin or the butterfly key.

## Licence and legal

- The artwork is © 2026 Carlos Canabal Cordero, MIT-licensed: see `LICENSE-ART.md`.
- Not affiliated with or endorsed by Blizzard Entertainment.
- Trademark clearance pending (USPTO/EUIPO/WIPO, classes 9 and 42). Do not register or claim the mark until that search is done.
