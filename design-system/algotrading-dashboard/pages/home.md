# Page: Console (`/`, `wwwroot/home.html`)

> Per MASTER.md's override rule, the rules here **replace** the Master file's colour and
> typography sections for this page only. Everything under `index.html` still follows MASTER.md.

**Role:** entry page. Answers one question — *is the system ready and running right now?* — then
routes into the dashboard. Served at `/`; the dashboard itself is at `/index.html`.

**Why it departs from the dashboard tokens:** the dashboard is instrumentation and stays flat,
dense and neutral. This page is the front door and carries the identity. Sharing a palette would
have forced one of the two to compromise. The only thing they share is the `theme` localStorage
key, so the light/dark toggle carries across.

---

## Colour

Files: `wwwroot/home.css` `:root` and `:root[data-theme="light"]`.

| Role | Dark | Light | Variable |
|------|------|-------|----------|
| Canvas | `#080b12` | `#f1f4f9` | `--ink` |
| Surface | `#0e1320` | `#ffffff` | `--ink-2` |
| Surface hover | `#141b2b` | `#e8edf5` | `--ink-3` |
| Hairline | `#1e2839` | `#d2d9e6` | `--rule` |
| Text | `#e9eef7` | `#101725` | `--bone` |
| Secondary | `#78859c` | `#566275` | `--dim` |
| Session live | `#ffb44a` | `#9a5906` | `--saffron` |
| Session dormant | `#7c89ff` | `#4149cf` | `--indigo` |
| Flowing / healthy | `#38d9a4` | `#087550` | `--jade` |
| Behind / down | `#ff5c72` | `#d12a44` | `--ember` |

The light values for `--saffron` and `--jade` are darker than their dark-theme counterparts on
purpose — they carry 11px eyebrow and 12px stat text, which needs 4.5:1 against a light canvas.
Measured: saffron 5.01 on canvas / 5.52 on card, jade 5.72 on card. Don't lighten them back.

### The accent is the session phase

`--accent` is not a fixed brand colour. `home.js` sets `data-phase` on `<html>` and CSS maps it:

| `data-phase` | When | `--accent` |
|---|---|---|
| `pre` | before 09:15 IST on a Normal day | `--indigo` |
| `open` | 09:15–15:30 | `--saffron` |
| `late` | exchange reports `PreClosed` | `--saffron` |
| `closed` | after 15:30 | `--dim` |
| `off` | holiday, weekend, or day not gated | `--dim` |
| `down` | any container not `running` — outranks the clock | `--ember` |

Anything new that should move with the trading day keys off `--accent`, not off a literal.

## Typography

| Role | Face | Setting |
|------|------|---------|
| Display | Archivo (variable) | `wdth 115, wght 800`, `-0.035em`, `clamp(2.6rem, 7.4vw, 5.4rem)` |
| Body | Inter Tight | 400/500, 16px |
| Data & labels | Fira Code | 500, tabular-nums, uppercase eyebrows at `0.22em` |

Every number, timestamp, eyebrow and stat is Fira Code — same face MASTER.md nominates for the
dashboard's numeric columns, which is the one deliberate thread between the two surfaces.

## The signature element

The **session ribbon**: one rail per contract per timeframe, all sharing a single 09:15→15:30
axis, with one vertical `now` rule cutting through every rail. Filled = bars ingested, ember =
the shortfall against the backend's `ExpectedSoFar()`, dim = the rest of the session. A contract
falling behind its neighbours is visible without reading a number.

Rails are three stacked full-width tick layers clipped with `clip-path: inset(...)`. Keeping every
layer full width is what locks the tick period to the axis instead of stretching it with the fill —
don't reimplement this by sizing the fill element's width. Below 760px the 375-tick period falls
under a device pixel, so `.layer` drops to a solid bar.

Rails are **updated in place** between polls, never rebuilt — the `clip-path` transition needs the
same element or the fill jumps. Same reason the route cards are built once: re-rendering the grid
every 5s would throw away keyboard focus.

**`.layer--fill` never keys off `.track--behind`.** It used to (`.track--behind .layer--fill` and
`.track--thin.track--behind .layer--fill` both forced it to `--ember`), which retroactively
repainted every already-ingested bar red the moment the aggregate ratio dipped, then flipped the
whole run back a few seconds later once the backend caught up — on the session ribbon that meant
several contracts' worth of already-correct bars flashing red together for no real reason. Fill
stays `--phase` (1-min ingestion rows) or `--jade` (aggregation/"thin" rows) regardless of status;
the gap layer already carries the "behind" signal on its own, for exactly the bars that are
actually missing. Same fix, same reasoning, as the Data page's `.rail-layer.fill` — see
`pages/data.md`'s "Status colour never repaints arrived data". Do this one first if you're touching
both files, since it's the same bug in two places.

## Rules for this page

- ✅ New accent-bearing elements use `--accent`, so they move with the session
- ✅ Numbers, labels and timestamps in Fira Code with `tabular-nums`
- ✅ All time computed explicitly in IST via `Intl` with `timeZone: "Asia/Kolkata"` — matches the
  backend's `IstNow()` discipline, correct from any viewer timezone
- ✅ Endpoints degrade individually: a dead endpoint says so, it doesn't blank the page
- ❌ No new colours outside the table above
- ❌ No second signature — the ribbon is the one memorable object; keep everything else quiet
- ❌ Don't add the dashboard's `style.css` here, or MASTER.md's tokens will fight these
