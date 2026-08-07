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

## Rule group hierarchy — two tiers, not one

"Trading Session Rules", "Long Entry" and "Short Entry" use `.rules-group-label--highlight`, same
as Data's "Data Ingestion (1-min)" / "Aggregation" and Exchanges' "Country" / "Exchange Session
Timeline" — see `pages/data.md`. One tier below that, "Entry Rules" / "Risk Management Rules" /
"Update Stop-Loss Rules" / "Exit Rules" use `.rules-section-title` (Archivo, 13.5px, `--text` —
`.candle-row-name`'s exact treatment), passed as `ruleListReadonlyHtml()`'s / `ruleSectionBlockHtml()`'s
optional third argument when the caller wants the highlighted variant instead.

**Sub-section titles don't repeat "Long Entry:"/"Short Entry:" anymore** — `LONG_ENTRY_SECTIONS`/
`SHORT_ENTRY_SECTIONS` in `app.js` just say `"Entry Rules"`, `"Risk Management Rules"`, etc. That
repetition existed because the group label above it used to be a plain `.rules-group-label`
eyebrow (11px, `--muted`) sitting *below* the sub-section titles in visual weight — a bare
`.view-label` picked up the ambient body-text size by cascade accident and read stronger than the
eyebrow meant to be its parent. Every sub-heading had to say "Long Entry: Entry Rules" just to
stay legible on its own, because the real group heading couldn't carry it. Making the group label
the visually dominant one is what let the repetition go.

## Bugs found while fixing the above

Screenshotting this page cold (landing straight on `#strategy`, not navigating there via nav
click) surfaced two pre-existing bugs, unrelated to the styling above but caught while verifying
it:

- **`STRATEGY_API_BASE` was declared past the code that reads it.** `showPage()`'s initial call
  (triggered on every page load when the URL hash is already `#strategy`) runs `loadStrategyGrid()`
  synchronously before the rest of the script has executed. `const STRATEGY_API_BASE = ...` used
  to sit ~700 lines further down, so this threw `Cannot access 'STRATEGY_API_BASE' before
  initialization` on every cold load — the grid silently rendered empty. Moved the declaration to
  the top of `app.js`, next to `REFRESH_MS`.
- **A stray `loadStrategyList();` call at the very bottom of `app.js`** referenced a function that
  doesn't exist (the real one is `loadStrategyGrid`) — dead code from a rename, redundant with (and
  directly contradicted by) its own neighboring comment: *"initial load … is handled by
  showPage() above."* Deleted.

Both were invisible from the UI unless you loaded `#strategy` directly rather than clicking there
from another page — worth remembering next time something on this page "works when I click to it
but not on refresh."

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
- [ ] Group titles (`.rules-group-label--highlight`) stay visually dominant over sub-section
      titles (`.rules-section-title`) — if that inverts again, the "Long Entry: …" repetition
      comes back for the same reason it did before
- [ ] New page-load-triggering code that reads a `const` declared later in `app.js` will hit the
      same TDZ crash `STRATEGY_API_BASE` did — declare early if `showPage()`'s initial call can
      reach it
