# Motion & effects layer (`wwwroot/fx.css`, `wwwroot/fx.js`)

> Loaded by **both** `home.html` and `index.html`, after their own stylesheets so the palette
> block wins. Applies to every page automatically — there is nothing to opt into per page.

**Stack:** none. No npm, no bundler, no React. Framer Motion is React-only and was not usable
here; Motion One and GSAP have standalone builds but bought nothing this brief needed. Everything
is hand-rolled against the platform: one `requestAnimationFrame` loop, Web Animations API for
one-shots, CSS custom properties for state.

**Measured:** 60 FPS, p95 frame 16.8ms, **zero** frames over 17ms on both the console and the
dashboard while the pointer is driven in a continuous arc.

---

## Palette

Brief-specified, dark-first. `index.html` and `home.html` now stamp `data-theme="dark"`
unconditionally unless the user has chosen otherwise — the OS preference no longer flips a
trading terminal to light mid-session.

| Role | Dark | Light | Why light differs |
|---|---|---|---|
| Accent | `#4F8CFF` | `#1746C4` | |
| Bullish | `#00E676` | `#006B42` | |
| Bearish | `#FF4D6D` | `#BC1A3D` | |
| Gold | `#FFC857` | `#7D5100` | |

The four brand colours measure **1.4–2.9:1** on the light canvas — unusable. The light column is
the darkened equivalent, measured composited. Floors after the change: dark 5.27 (stale pill),
light 4.99 (stale pill). Don't use the dark values in light mode.

## Intensity — `data-fx` on `<html>`

Cycled by the header button (`#fx-toggle`), persisted to `localStorage.fx`, stamped before first
paint so there is no flash of full motion for someone who turned it down.

| Level | Behaviour |
|---|---|
| `full` | everything (default) |
| `calm` | no canvas, no cursor glow, no continuous background motion; hover, entrance and state transitions still run |
| `off` | no effects; `#fx-root` removed from the paint entirely |

`prefers-reduced-motion: reduce` defaults to `calm` and additionally collapses every duration to
1ms. `@media (hover: none)` drops cursor effects and tilt — there is no pointer to track.

## What's in it

**Background** (`#fx-root`, one canvas + CSS layers): drifting grid with pointer parallax, three
aurora blobs on mutually prime 71/89/103s durations so the composite never visibly loops, three
sheared light beams, a particle field with pointer repulsion and proximity links, a slow market
polyline with gradient area fill, pointer spotlight, vignette.

**Pointer:** glow + ring + 7-point trail, each with its own smoothing half-life. **The native
cursor is never hidden** — precision and the resize/text/pointer shape cues matter more on a
terminal than a custom dot. Magnetic pull on buttons and nav items is capped at 6px: enough to
feel alive, not enough to make you miss the target.

**Surfaces:** 3D tilt (max 5.4°), pointer-tracked sheen, conic border-light, staged entrance,
idle float offset per card so they never sync.

**Data:** value-change flash green/red with a ▲/▼ delta, count-up numbers, sparklines, sticky
table headers, row hover, skeleton shimmer.

**Chrome:** sliding sidebar indicator, page transitions (fade + slide + scale + blur), toasts with
progress bar and type-coloured glow.

### Frame-rate independence

Smoothing uses `target + (cur - target) * 2^(-dt/halfLife)`, not a fixed per-frame lerp. Inertia
then feels identical at 60 and 144 Hz instead of running twice as fast on the faster display.

### Two things that are easy to get wrong

1. **`--muted`, `--panel`, `--border`, `--text`, `--bg` are style.css names.** `home.css` calls
   the same roles `--dim`, `--ink-2`, `--rule`, `--bone`, `--ink`. `home.css` now aliases the
   former onto the latter so `fx.css` can address one vocabulary. Without the aliases every
   fx-styled element on the console silently falls back — widget labels rendered at full text
   brightness instead of muted. If you add a token to one file, alias it in the other.

2. **The value-flash allowlist deliberately excludes the freshness Age column.** It ticks on
   every poll for every row; flashing it would strobe the whole table twice a minute and mean
   nothing. Flash only where a change carries information.

## Component marks (console widgets)

Each System widget carries a mark. Two kinds, both riding `currentColor` so they take the
widget's state colour:

| | Source | Rendering |
|---|---|---|
| **Vendor marks** — Kafka, Redis, RabbitMQ, .NET, Docker | Simple Icons (CC0), fetched not transcribed | solid `fill: currentColor` |
| **Own glyphs** — everything else | the app's `ICONS` set | stroked, 1.6 weight |

**Azure Functions, Azurite and SignalR use glyphs on purpose.** Microsoft had its marks withdrawn
from Simple Icons, so there is no accurate source. Drawing a trademark from memory produces a
wrong logo, which is worse than an honest glyph — don't.

### viewBox is cropped per mark

The five vendor marks range from **0.61** aspect (Kafka, tall) to **2.68** (.NET, a wordmark). In
a shared square slot the .NET mark squashed to about a fifth the height of the others. Each
`LOGOS` entry therefore carries a `box` cropped to its true path bounds, and `.widget__logo` is
sized by **height with `width: auto`** (capped at 26px). Every mark then lands at a matching 15px
optical height and takes whatever width its shape needs. If you add a logo, measure its `getBBox()`
and record the crop — don't assume `0 0 24 24`.

### Brand tints are theme-aware

Marks rest in `--muted` (healthy ones a step brighter) and lift to the brand hue on hover. The
label already names the service, so the colour is affordance, not information.

Full-saturation brand hues fail on a white panel: RabbitMQ orange measured **2.86** and Docker
blue **3.08**, under the 3:1 floor for graphical objects. `--brand-*` tokens carry darkened light
variants. Floor after the fix: **4.18** across both themes.

Kafka's mark is black-on-white by brand, so `--brand-kafka` rides `--text` rather than a fixed hue.

## Not built, and why

| Asked for | Status |
|---|---|
| CPU / RAM widgets | Needs a `/api/stats` endpoint. Docker's stats API can supply it — not yet written. |
| RabbitMQ | Not in this stack. It runs Kafka. |
| PnL / Risk / Orders / Positions | No execution engine exists. Rendered in an explicit unwired state — **never filled with placeholder numbers.** |
| Candle chart, crosshair, volume bars, buy/sell markers | OHLC exists in Redis and Azurite but no endpoint serves a bar series to the browser. |
| BUY/SELL particle burst | Built and callable as `FX.burst(x, y, 'buy'\|'sell')`, but nothing binds it — there is no order fill to hang it on. Wire it at the fill site. |

**The rule that produced that table:** a trading dashboard must never show a number it cannot
source. An invented PnL is worse than an empty one.

## API

```js
FX.level('full'|'calm'|'off')   // also the header button
FX.toast({ title, body, kind: 'ok'|'bad'|'warn', duration })
FX.countUp(el, value, { decimals, prefix, suffix })
FX.spark(svgEl, values)
FX.burst(x, y, 'buy'|'sell')
FX.enhance(root)                // re-stage a subtree after it re-renders
```
