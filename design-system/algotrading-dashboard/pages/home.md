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
axis, with one vertical `now` rule cutting through the whole ribbon. Jade = bars ingested, ember =
buckets expected by now that genuinely aren't there, dim = the rest of the session not due yet. A
contract falling behind its neighbours is visible without reading a number — and, as of the
per-bucket rewrite below, so is exactly *where* in the day it fell behind.

Rails went through two designs. The first was three stacked full-width tick layers, `count`-many
ticks clipped as one contiguous green run from the start, one contiguous ember gap after it. That's
an aggregate approximation — a Redis SET's cardinality has no position, only membership — and it
breaks visibly the moment a day isn't shaped "healthy, then behind": an outage in the middle of an
otherwise-fine day rendered as one trailing gap wherever the arithmetic happened to put it, never
where the real hole was, and recolored every already-arrived bar for as long as the aggregate ratio
stayed low regardless of how well things were actually going by the time you looked.

**`.track__map` now paints real per-bucket ground truth.** The backend's `bucketMap` (`Program.cs`,
shared with the Data page) is one character per expected bucket — `'a'` arrived, `'m'` missing,
`'p'` not due — read from the count SET's actual *members* (each one is that bucket's own
`WindowsStartTime`), not just its length. `bucketMapGradient()` (`home.js`) turns that into a
run-length-encoded `linear-gradient` — one color-stop pair per state *transition*, not per bucket —
set as `.track__map`'s `background-image`. The tick rhythm is a separate `mask-image` on
`.track__map`, sized to `calc(100% / var(--bars))`, independent of color. Below 760px, drop the
mask (`mask-image: none`) rather than the old clip-path fallback.

Three fixed colors, never a status- or session-driven palette: arrived is **always** `--jade`,
missing is **always** `--ember`, pending is **always** `--rule`. Two things this replaces, both
real bugs that shipped:

- **`.track--behind` used to force the whole fill to `--ember`** whenever the aggregate ratio
  dipped, retroactively repainting every already-ingested bar as if it were a problem — several
  contracts' worth of correct bars flashing red together for no real reason, then flipping back a
  few seconds later once the backend caught up. Not representable the same way anymore: color is
  now a property of each bucket's own arrived/missing state, not an aggregate applied to the whole
  element.
- **Fill used to be `--phase`** (shifting with session mood — indigo pre-open, amber during open,
  muted after close) **for the 1-min row, `--jade` for `.track--thin` (aggregation) rows.** Two
  colors for one fact, and the legend's single "ingested" swatch only ever matched one row out of
  eight. `--phase` is for chrome that should move with the trading day — the page wash, the mark —
  not for "this data arrived," which doesn't stop being true because the market closed. `--jade` is
  fixed now, matching the Data page's `--green`. See `pages/data.md`'s "Three fixed colors."

Rails are **updated in place** between polls, never rebuilt — the same element needs to persist for
`background-image` to have any chance of transitioning smoothly, and for the same reason the route
cards are built once: re-rendering the grid every 5s would throw away keyboard focus.

## Rules for this page

- ✅ New accent-bearing elements use `--accent`, so they move with the session
- ✅ Numbers, labels and timestamps in Fira Code with `tabular-nums`
- ✅ All time computed explicitly in IST via `Intl` with `timeZone: "Asia/Kolkata"` — matches the
  backend's `IstNow()` discipline, correct from any viewer timezone
- ✅ Endpoints degrade individually: a dead endpoint says so, it doesn't blank the page
- ❌ No new colours outside the table above
- ❌ No second signature — the ribbon is the one memorable object; keep everything else quiet
- ❌ Don't add the dashboard's `style.css` here, or MASTER.md's tokens will fight these
