# Page: Strategy (`index.html#strategy`)

> Per MASTER.md's override rule, the rules here **replace** the Master file where they conflict.

---

## Stacked rows, not a table

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

## Panel and forms

`.strategy-panel-head h3` is Archivo. `.form-field label`, `.view-label` and `.rule-row-title`
are all eyebrows now (10px / `0.14em` / `--muted` / weight 500), so the panel matches the rest of
the system.

---

## Light-theme tints — read this before touching the palette

Pills, badges and `.btn-primary`/`.btn-danger` put text on a tint **of its own hue**
(`--green` on `--green-bg`, etc.). Same-hue tinting costs roughly half a contrast point, and at
10px these need 4.5:1. Two consequences, both deliberate:

- Light tint alphas are **0.08**, not 0.12/0.10. Raising them pushes badges back under AA.
- Light `--red` is **`#c62038`**, darker than you'd pick by eye against the dark theme's
  `#ff5c72`. `home.css`'s `--ember` was moved to match so the two files stay identical.

Measured floors after the change — light: badge 5.11, `.btn-danger` 5.04, `th` 5.60. Dark: badge
7.68, `th` 5.28.

## Checklist

- [ ] Deployment state stated in text, not by colour alone
- [ ] Strategy meta omits unset fields rather than printing em-dashes
- [ ] Light tints stay at 0.08 alpha; re-measure any badge colour change **composited**,
      not against the raw token
