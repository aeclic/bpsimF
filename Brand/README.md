# BPSim brand assets

The BPSim identity is a **bespoke wordmark**, not an emblem. It is built the way
the [DeepSeek-Reasonix](https://github.com/esengine/deepseek-reasonix) logo is
built — a hand-drawn geometric grotesque wordmark whose letter ink doubles as a
window into a moving body of water (`docs/logo-ghost-wave-effect.svg`) — but the
mascot living inside the hero letter is a **pig**, not a whale.

| element | Reasonix | BPSim |
| --- | --- | --- |
| identity | custom wordmark, one path per letter | custom wordmark `bpsimF`, one path per letter |
| hero letter | `R` with a whale + wave carved out of its solid bowl | `b` with a pig face carved out of its solid bowl |
| fill | letters filled by a rising sea, ghost above the waterline | same |
| accent | solid blue i-dot, outside the clip | solid amber i-dot + solid amber `F` |

## 1. Construction

Every glyph is drawn from a skeleton and buffered to an outline, so all letters
share one stroke and one set of terminals.

| metric | value |
| --- | --- |
| stroke `S` | 37.6 |
| cap height | 214.57 |
| x-height | 142.52 (top at 72.05) |
| descender | 280.0 |
| overshoot | 2.5 |
| bowl radius (centreline) | 54.96 |
| tracking | 13.0 + per-pair kerning |
| ink bounds | 913.25 x 280 (advance width 932.0) |

Ink gaps at the x-height midline: `bp` 16.0, `ps` 11.0, `si` 12.2, `im` 13.7,
`mF` 19.9 — deliberately tight, like the reference.

Design details inherited from the reference: flat horizontal terminals on stems,
angled cuts on curves, m arches that spring from the stem with a 2 unit overlap
(no hairline seams), and the letter b keeping a 0.5 unit flat-foot step where the
bowl overshoots the stem — a laser-cut tell, not a bug.

### The pig in the `b`

The `b`'s counter is filled solid and a pig face is carved out of it, exactly like
the whale in the Reasonix `R`:

1. `b` union a disc (counter radius + 1) — the bowl becomes a solid slab,
2. minus the pig silhouette (round head, two upright ears, jaw),
3. plus two eye islands and a snout island,
4. minus two nostril holes.

Four nesting levels in one nonzero path (islands are emitted CCW, holes CW). The
pig is 94 units wide against a 72.3 unit counter, so it bleeds into the stem the
same way the reference whale erodes its `R` — the reference `R` stem is 55 wide and
is eroded to ~25 by a 138 wide hole (72 % of the letter); our `b` stem survives at
~36 (71 % of the letter).

## 2. Colour

```
sea (vertical)   #7dd3fc -> #3b82f6 (42 %) -> #6366f1
amber accent     #fcd34d -> #f59e0b (55 %) -> #f97316
ghost            #38bdf8 at 16 %
bubbles / foam   #e0f2fe, #a5f3fc
ink / background #0a0d14
muted text       #64748b
```

Rule of use: **cool for the letterform, one warm accent only** (the i-dot and the
`F`). Never rainbow the letters — the earlier explorations that spread three hues
across the mark read as gamer-RGB rather than as engineering.

## 3. Files

`svg/` — the masters (no live text, no fonts required):

| file | use |
| --- | --- |
| `bpsim-wordmark-animated.svg` | the signature asset: sea rise, waves, bubbles, dot hop, F pop |
| `bpsim-wordmark-static.svg` | flat colour wordmark |
| `bpsim-wordmark-mono-white.svg` | one-colour white, transparent |
| `bpsim-wordmark-mono-ink.svg` | one-colour ink, for light backgrounds |
| `bpsim-icon-animated.svg` | 512 app tile: aquarium `b` with live water |
| `bpsim-icon-static.svg` | same, frozen |
| `bpsim-icon-flat.svg` | single-colour tile for small sizes |
| `bpsim-icon-mono-white.svg` | white mark, transparent (favicons, watermarks) |
| `bpsim-watermark-2048x1152.svg` | white wordmark, transparent plate |
| `bpsim-hero-2048x1152.svg` | dark hero plate with tagline |

`png/` — rasters (2x supersampled, Lanczos downscaled):

`bpsim-wordmark-static-1600/800/400`, `bpsim-wordmark-mono-white-1600/800`,
`bpsim-wordmark-mono-ink-1600/800`, `bpsim-icon-512/256/128` (detail),
`bpsim-icon-64/48/32/16` (flat), `bpsim-icon-mono-white-512`,
`bpsim-watermark-2048x1152` + `-1024x576`, `bpsim-hero-2048x1152`,
`bpsim-still-rise-1600`, `bpsim-still-settled-1600` (animation stills),
`bpsim-wordmark-loop.gif` (480 px preview loop: settled, dip to ink, sea rises).

## 4. Animation

Timings in `bpsim-wordmark-animated.svg` (CSS only, no JavaScript):

| layer | motion |
| --- | --- |
| letters (clip) | sea rises `translateY(310 -> 0)`, 2.0 s, cubic-bezier(.22,.9,.32,1), delay .15 s |
| wave A | `url(#sea)` body scrolls left, 5.2 s |
| wave B | lighter body scrolls right, 7.6 s |
| currents | four foam bands, 7.8-12.5 s, plus a 9 s tide bob |
| bubbles | seven bubbles rise and fade, 7.2-9.6 s |
| glint | skewed white sweep, 7.5 s, starting at 2.6 s |
| i-dot | solid amber, hops 2.6 s (delay 2.2 s) |
| F | solid amber, pops in at 1.7 s, glint at 3.4 s |

`prefers-reduced-motion` disables everything and shows the settled state. Nothing
rotates: the identity is typography, so the only motion is the medium inside the
letters (water, bubbles, dot) and the light across them.

The icon animates the same vocabulary inside the tile: the `b` is a vessel of
water with a waterline at 34 glyph units, the stem holding bubbles, three current
bands scrolling at different speeds, and the pig sitting in the counter as dry
negative space.

## 5. Usage

- **Clear space**: the x-height (142 units) on all four sides of the ink box.
- **Minimum sizes**: wordmark 240 px wide (pig still reads); icon 32 px (flat
  variant below 128 px — the detailed tile turns to mush).
- **Do not**: recolour individual letters, outline the wordmark, stretch it,
  rotate the mark, place the mono-ink version on dark backgrounds, or use the
  animated file in static contexts (use the stills).
- The wordmark is **lowercase with a capital F**: `bpsimF`. The F is a version
  suffix (the F-line of BPSim), so it is set as a solid accent block rather than
  a lighter letter.

## 6. Regenerating

**The SVGs in `Brand/svg` are the masters.** Every outline inside them is a
hand-fitted path — the `s` was fitted from the reference wordmark's medial axis,
the pig is a boolean composition, the letterforms are buffers of stroke
skeletons — and the animation is CSS authored inside the animated file. No font
is referenced and no build step is required to use them.

To (re)rasterise the PNG set, or to add a new size, edit the table in
`Tools/Brand/render.py` and run:

```bash
cd Tools/Brand
python render.py                 # every target
python render.py icon-128        # one target
```

`render.py` renders through headless Chrome at a device scale factor for
supersampling, then Lanczos-downscales to the exact pixel size and keeps the
alpha channel. Two pitfalls it already handles:

- Chrome clamps very large `--window-size` values, so the CSS box is always
  `width / scale` — a 2048 px output renders into a 1024 px box at
  `--force-device-scale-factor=2`. Without this the render silently crops;
- headless Chrome cannot advance CSS animations, so `bpsim-still-rise`,
  `bpsim-still-settled` and the preview GIF are deterministic frozen frames, not
  captures of the live animation. Open the `.svg` in a browser for the real
  motion, or drive the same keyframes from a script to re-shoot the frames.

Geometry reference, if an outline ever needs to be extended:

| metric | value |
| --- | --- |
| stroke | 37.6 |
| cap height | 214.57 |
| x-height | 142.52 (top at 72.05) |
| descender | 280.0 |
| bowl centreline radius | 54.96 |
| overshoot | 2.5 |
| tracking | 13.0 + per-pair kerning (`bp` +3, `ps` -2, `si` +3, `im` +7, `mF` +7) |
| ink bounds | 913.25 x 280, advance width 932.0 |

## 7. In-engine use

`Assets/Texture2D/Bpsim_Logo_Transparent.png` (guid
`4b4262196dac3a04c8c989e80b19b104`, 2048x1152, white on transparent) is the
start-page watermark shown by the `RawImage` in
`Assets/GameObject/StartPage.prefab` over a black background. It is regenerated
from `bpsim-watermark-2048x1152.png`; the `.meta` file (and therefore the GUID
and every reference to it) must never be rewritten.

The 512 px icon is ready for the player icon / store art
(`Brand/png/bpsim-icon-512.png`); it is not wired into `ProjectSettings.asset`
because the player icon list is a per-platform build setting.
