# Pages: Data Freshness & Strategy

> Per MASTER.md's override rule, the rules here **replace** the Master file where they conflict.

---

## Data Freshness (`index.html#freshness`)

A table, and it stays a table — seven short columns of the same shape per row is exactly what
tables are for. What changed is the treatment.

- **Column heads are eyebrows:** 10px, uppercase, `0.14em`, `--muted`. Same as every other label.
- **Row hover** tints to `--panel`, matching the card hover elsewhere.
- **Stale rows** tint the ticker cell `--red`. The `Stale` pill is still the primary signal —
  the tint is a scanning aid layered on top, never the only indicator.

### The age rail

Each Age cell carries a 46px bar showing age against **that key's own stale threshold**, which
the backend sets at `2 × timeframe`. A 1-min key and a 60-min key are held to different clocks,
so a raw age is not comparable down the column; a percentage of each key's own threshold is. The
bar fills `--green` and flips `--red` once the row is stale.

This is the session rail's idea at row scale. Keep it subtle — 3px tall, no label; the number
next to it is the readable value.

### Timestamps

`stampTime()` slices `DD/MM HH:MM:SS` out of the ISO string with a regex. The previous code used
`new Date(iso).toLocaleString()`, which is wrong here: Redis writes these as **IST-local strings
with no zone suffix**, so `Date` re-reads them in the viewer's timezone and shifts every row.
Same rule as `clockTime()` on the Data page — never parse these as dates.

---

## Strategy (`index.html#strategy`)

### Stacked rows, not a table

The grid was a 10-column table with `min-width: 900px` and a horizontal scrollbar. Most of those
columns were empty for most strategies, and every unset field rendered an em-dash, so a
half-configured strategy read as broken rather than simply unfinished.

It's now one `.strategy-row` per strategy, same shape as the Data page's contract rows:

```
Second Income  [v1.0.6]  [Deployed v1.0.6]        View  Edit  Deploy  Delete
NSE · Zerodha · Intraday · risk Moderate · Nifty 50 Options
```

Name in Archivo; version chip reuses `.candle-tf`; deployment state is one badge that states the
version rather than a bare "Current"/"Behind" next to a separate version column. The meta line
joins **only the fields that are set** — an empty strategy says "Nothing configured yet" instead
of a row of dashes.

Actions stay as four buttons bound by `[data-action]` delegation in `loadStrategyGrid`, so the
view/edit/deploy/delete wiring is unchanged.

### Panel and forms

`.strategy-panel-head h3` is Archivo. `.form-field label`, `.view-label` and `.rule-row-title`
are all eyebrows now (10px / `0.14em` / `--muted` / weight 500), so the panel matches the rest of
the system.

---

## Light-theme tints — read this before touching the palette

Pills, badges and `.btn-primary`/`.btn-danger` put text on a tint **of its own hue**
(`--green` on `--green-bg`, etc.). Same-hue tinting costs roughly half a contrast point, and at
10px these need 4.5:1. Two consequences, both deliberate:

- Light tint alphas are **0.08**, not 0.12/0.10. Raising them pushes the pills back under AA.
- Light `--red` is **`#c62038`**, darker than you'd pick by eye against the dark theme's
  `#ff5c72`. At the old `#d12a44` the stale pill measured 3.98:1 — a real failure, not a
  rounding issue. `home.css`'s `--ember` was moved to match so the two files stay identical.

Measured floors after the change — light: pill 4.58, badge 5.11, `.btn-danger` 5.04, stale key
5.20, `th` 5.60. Dark: pill 5.59, badge 7.68, `th` 5.28.

## Checklist

- [ ] Timestamps through `stampTime()`/`clockTime()`, never `new Date()`
- [ ] Stale/deployed state stated in text, not by colour alone
- [ ] Age rail compares against the key's own threshold, not a fixed one
- [ ] Strategy meta omits unset fields rather than printing em-dashes
- [ ] Light tints stay at 0.08 alpha; re-measure any pill/badge colour change **composited**,
      not against the raw token
