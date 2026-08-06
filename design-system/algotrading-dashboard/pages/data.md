# Page: Data (`index.html#data`)

> Per MASTER.md's override rule, the rules here **replace** the Master file where they conflict.

**Role:** show how far each contract has got through today's session, against how far it *should*
have got by now. This is the detailed view of the console's session ribbon and deliberately
reuses the same object.

---

## Layout: stacked rows, not a card grid

`.candle-grid` is a vertical `flex` stack, one `.candle-row` per contract per timeframe. It used
to be `grid-template-columns: repeat(auto-fill, minmax(240px, 1fr))`.

Two reasons it had to change, both load-bearing:

1. **Tick resolution.** The rail draws one tick per expected bar — 375 for the 1-min feed. In a
   240px card that's 0.64px per tick, i.e. mush. Full-width rows give ~890px, or 2.4px per tick.
2. **Comparison.** Every row now shares the same 09:15→15:30 axis, so a contract falling behind
   its neighbours is visible by eye. That is the whole point of the console's ribbon, at detail.

Don't put these back in a multi-column grid.

## The rail

Same mechanics as `home.css`'s `.track__rail` — three full-width tick layers, each clipped to its
share of the session:

```
.rail          --bars (tick count) · --fill (count %) · --exp (expectedSoFar %)
  .rail-layer.rest   --border      the full session
  .rail-layer.gap    --red @ .5    clipped to --exp   → shows through as the shortfall
  .rail-layer.fill   --green       clipped to --fill  → what actually arrived
  .rail-now          --text 1px    sits at --exp
```

**Every layer stays full width; only `clip-path` differs.** That's what locks the tick period to
the axis. Sizing the fill element's `width` instead would stretch the ticks and the bars would no
longer line up between rows. `background-size: calc(100% / var(--bars))` gives exactly `--bars`
periods across the track.

`.rail[data-status]` recolours the fill: `amber` → `--yellow`, `red` → `--red`, `pending` →
`--muted`. Status comes from the backend's `ComputeStatus`, which compares against `ExpectedSoFar`
— not against the full 375 — so "behind" only lights once it genuinely is.

Below 760px the tick period drops under a device pixel, so `.rail-layer` falls back to a solid
bar. Keep that media query if you change the rail.

## Row anatomy

```
NIFTY [1m]                              [On Track]  253 / 375 bars
▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
09:15          1 behind · 254 expected by now          15:30
────────────────────────────────────────────────────────────
TradingView · latest 14:12:00 · updated 14:13:02   [●Redis] [●Azurite]
```

Contract name in Archivo; everything else Fira Code with `tabular-nums`, because all of it
updates every 5s and proportional figures make the row jitter.

Timestamps go through `clockTime()` — Redis stores them as IST-local strings with no zone suffix,
so `new Date(...)` would shift them into the viewer's timezone.

## Instrument color

`.candle-row-name` gets its color from `instrumentColorVar()` in `app.js`, which picks one of
MASTER.md's five `--tag-*` categorical tokens **in first-discovery order**, cached in a `Map` for
the page's lifetime — not hashed. A hash has no collision guarantee: BANKNIFTY and NIFTY, the very
first two tickers this shipped with, landed on the identical tag under a plain string hash, which
defeats the entire point. First-seen ordinal assignment guarantees every distinct ticker gets its
own color as long as ≤5 are on screen at once. Same ticker → same color on every card, in both
Ingestion and Aggregation, without ever hand-mapping a specific ticker to a specific color — the
Services page's old `CATEGORIES` color scheme took that shortcut and it's exactly why a container
silently fell into the wrong bucket when nobody updated the map.

`--tag-*` is identity-only, never status — don't reach for it to mean "behind" or "stale"; that's
still `--red`/`--yellow` via the rail and the badge.

## Section titles

"Data Ingestion (1-min)" and "Aggregation" use `.rules-group-label--highlight` instead of the
plain `.rules-group-label` that Exchanges' "Country" / "Exchange Session Timeline" use. This page
has no per-subsection `h2` to lean on the way a page boundary does, so these two carry more visual
weight: Archivo instead of the Fira Code eyebrow face, `--text` instead of `--muted`, an `--accent`
dot and top rule. Scoped to this page only — don't apply the modifier class to `.rules-group-label`
elsewhere (Exchanges, the Strategy rule builder's Long/Short Entry labels) without deciding that's
wanted there too.

## Checklist

- [ ] Rows stay full-width and stacked
- [ ] Rail layers stay full width; only `clip-path` varies
- [ ] `--bars` matches `expectedTotal`, so the tick count is the real bar count
- [ ] The `<760px` solid-bar fallback survives
- [ ] Shortfall stated in text (`N behind`), not by colour alone
- [ ] Timestamps through `clockTime()`
- [ ] Instrument color via `instrumentColorVar()`'s first-seen assignment, never a hash (no
      collision guarantee) or a hardcoded ticker→color map
- [ ] `--tag-*` used for identity only, never repurposed as a status color
