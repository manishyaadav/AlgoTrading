# Page: Exchanges (`index.html#exchanges`)

> Per MASTER.md's override rule, the rules here **replace** the Master file where they conflict.

**Role:** show the two gates the whole trading day hangs off — the country check that decides
whether there's a session at all, and the five fixed-time timers `exchange-live` fires through it.

---

## Country gate — `.status-card`

A plain panel: name in Archivo, state as a `.badge`, and a label/value meta block.

`.status-card-meta` rows are a **two-column grid**, not a run-on line. The values line up down
the card, and a screen reader reads "Date … 2026-08-06" rather than "Date2026-08-06". Don't
collapse it back to `<b>Label:</b> value` with a literal space. Below 480px it stacks.

Labels are `--muted`, values `--text` — the value is what you came for.

## Session timeline — three states, not two

This page is the detailed view of the console's `.gates` row, so it uses the **same diamond
vocabulary** rather than a second metaphor: an 11px square rotated 45°, hairline connectors.

| State | Class | Marker | Meaning |
|---|---|---|---|
| Passed | `.done` | filled `--green` | timer already fired today |
| Now | `.done.current` | filled `--accent` + 4px `--accent-bg` ring | the stage we're at |
| Ahead | `.pending` | hollow, `--border` | hasn't fired yet |

The old markup only had `done` / `pending`, so the current stage looked identical to the ones
behind it. `stageClass()` in `app.js` now emits all three. The stages fire strictly in order and
each overwrites the same Redis key, so "current index" reliably implies every earlier stage fired
— there's no need to track the five independently.

Connectors stay two-state (`done` up to the current stage) — a connector is the span *between*
gates, and it's either crossed or it isn't.

## Section titles

"Country" and "Exchange Session Timeline" use `.rules-group-label--highlight`, same as the Data
page's "Data Ingestion (1-min)" / "Aggregation" — see `pages/data.md`. Neither page has a
per-subsection `h2`, so these carry the weight one would otherwise: Archivo, `--text`, an
`--accent` dot and top rule, instead of the plain Fira Code eyebrow.

## Timestamps

Redis stores `UpdatedOn` as an **IST-local string with no zone suffix**. Use `clockTime()`, which
slices the time out with a regex. `new Date(...)` would silently re-interpret it in the viewer's
timezone and shift it. Everything on this page is one trading day, so the date carries no
information beyond the `Date` row.

## Checklist

- [ ] Three timeline states preserved — the current stage must not read as merely "done"
- [ ] Timestamps through `clockTime()`, never `new Date()`
- [ ] Meta stays a two-column grid
- [ ] Contrast checked both themes (current floor: 4.97 dark, 6.18 light)
