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

## The rail — per-bucket ground truth, not an aggregate approximation

The rail used to be a two-region model: `count`-many ticks rendered as one contiguous green run
from the start, then one contiguous gap. That's an *approximation* — `count` comes from a Redis
SET's cardinality, and a SET has no order or position, just membership. The approximation breaks
visibly the moment a day isn't a clean "healthy, then behind" shape: if the pipeline was down for a
stretch in the middle of the day and has been fine ever since, the true picture is
green…green…**red (the actual missed buckets)**…green…green, and the two-region model cannot draw
that. It can only ever show one trailing gap, positioned wherever the arithmetic puts it — never
where the real hole is.

The backend now sends `bucketMap`: one character per expected bucket, index 0 = session open —
`'a'` arrived, `'m'` missing (expected by now, genuinely isn't there), `'p'` not due yet. Built in
`BuildCandleCountStatus` (`Program.cs`) by reading the count SET's actual **members** (not just its
length) — each member is that bucket's own `WindowsStartTime`, written by
`DataIngestionNotificationFunctions`/`DataAggregationNotificationFunctions` — and checking, for
every bucket from session open to now, whether its own start-time string is a member. Bucket starts
are computed as `sessionOpen + i × timeframeMinutes`, which is exactly how
`RunningBucket.FloorToBucketStart` aligns buckets on the aggregator side, so this lines up
bucket-for-bucket with reality — not an approximation of it.

```
.rail
  .rail-map   background-image: linear-gradient(...)   the per-bucket colors
  .rail-now   left: var(--exp)                          where the session should be by now
```

`bucketMapGradient()` (`app.js`) turns the map into a **run-length-encoded** gradient — one
color-stop pair per state *transition*, not per bucket. A 1-min row with two transitions (arrived →
missing → arrived) costs 4 stops regardless of whether `expectedTotal` is 25 or 375. The tick
rhythm (the small gaps between buckets) is a separate `mask-image` on `.rail-map`, sized to
`calc(100% / var(--bars))` — independent of color, so it costs nothing extra no matter how
fragmented the map is.

**`.rail-now` is a sibling of `.rail-map`, never its child.** `.rail-map`'s mask applies to
everything it contains — nesting the now-marker inside it would chop the marker into the same tick
pattern instead of drawing one clean line.

Below 760px the tick period drops under a device pixel — drop the mask (`mask-image: none`) so it
reads as a solid multi-color bar instead of mush. Keep that media query if you change the rail.

## Three fixed colors, never a status- or phase-driven palette

Arrived is **always** `--green`, missing is **always** `--red`, pending is **always** `--border`.
Two things this replaces, both real bugs that shipped:

- **Status used to recolor the whole fill** (`data-status="amber"` → `.rail-layer.fill { color:
  var(--yellow) }`). The fill covers *every bar that already arrived*, so recoloring it
  retroactively repainted successfully-ingested history as a problem, then flipped it back a few
  seconds later once the aggregate caught up. Bars that landed didn't become wrong because a
  *later* bucket is running behind — and with per-bucket ground truth, this isn't even
  representable the same way anymore: color is a property of each bucket's own arrived/missing
  state, not of an aggregate status applied to the whole element.
- **The Data page's fill was `--green` while the console's was `--phase`** (shifting with session
  mood — indigo pre-open, amber during open, muted after close). Two colors for "this bar arrived"
  depending which page you were on, and on the console, arrived data literally changed color
  through the day for reasons unconnected to whether it had actually arrived. See
  `pages/home.md`.

The status badge (`On Track` / `Behind` / `Behind / No Data`) still carries the aggregate signal in
text, and still comes from `ComputeStatus`, which has been through two revisions, each fixing a
real failure this app
hit in production:

1. **Ratio → absolute bucket gap.** `count / expectedSoFar >= 0.9` made low-`expectedTotal`
   timeframes absurdly sensitive to completely normal lag — 75-min has only 5 buckets in a whole
   session, so being one bucket behind (routine right after every boundary, especially for the
   cascaded 10/15/30/60/75-min aggregators that wait on another aggregator's own bucket cycle
   before they can even start) is a 20% ratio swing that flipped straight to amber. The identical
   one-bucket lag on 1-min (375 buckets) barely dented its ratio.
2. **Cumulative gap → freshness of the latest arrival.** Even the fixed absolute-gap version
   (`expectedSoFar - count`) shared the ratio's real flaw: neither can recover from a *permanent*
   historical gap. If the pipeline is down for a stretch and misses, say, 40 buckets, `count`
   stays 40 short of `expectedSoFar` for the rest of the day — even once the pipeline has been
   perfectly healthy for hours since resuming. The badge stayed red forever, regardless of how
   well things were actually going right now.

`ComputeStatus` now takes `latestAgeSeconds` — how long since the most recent bar arrived — and
compares it against the same "stale" thresholds `/api/freshness` already uses (2×/4× the
timeframe): `green` if fresh, `amber` if moderately stale, `red` if very stale or nothing has
arrived at all this session. One shared definition of "stale" across the app, applied to whichever
bar is *most recent*, not a running total — a candle that landed 90 seconds ago means "caught up
right now," full stop, regardless of what happened three hours earlier. The cumulative gap is
still shown as informational text (`N short`) — that's accurate and worth keeping — it just no
longer drives the color.

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
plain `.rules-group-label`. This page has no per-subsection `h2` to lean on the way a page
boundary does, so these two carry more visual weight: Archivo instead of the Fira Code eyebrow
face, `--text` instead of `--muted`, an `--accent` dot and top rule. Exchanges' "Country" /
"Exchange Session Timeline" use the same modifier for the same reason — see `pages/exchanges.md`.
The Strategy rule builder's Long/Short Entry labels stay plain on purpose: they're one level down
from a section they're already inside, not a page-level heading.

## Indicators (EMA / Supertrend / Pivot Central Range)

Third sub-section under "Aggregation", below Timeframes — was a `.placeholder` block ("nothing
computes these anywhere yet") until `WarmUpService`'s cold-start seeding and `AggregationService`'s
live calculators shipped (see `WARMUP_AND_INDICATOR_PLAN.md` section 2e). `GET /api/indicators`
discovers cards straight from `Indicator:Running:*` in Redis — not from the manifest, since the
manifest only lists what `AggregationService` needs to keep live (Pivot Central Range is
deliberately excluded from it, having no live phase), but this section should still show PCR once
it's computed each morning.

Reuses the `.candle-row` shell wholesale (`.candle-row-head`, `.candle-row-foot`, `.candle-tf`,
`.badge`) — an indicator card is the same "name + timeframe + status badge, then content, then
meta footer" shape as an ingestion/aggregation card, just with a different middle section:
`.indicator-body` (the headline value, right-aligned, `Fira Code` tabular-nums at 22px — one size
up from `.candle-count` since this is the card's whole reason to exist, not a footnote on it) in
place of the bucket-map rail, since a single scalar value has no 375-bucket session to visualize.
A cold-start-in-progress instance (`isSeeded: false`) shows `.indicator-seed` — a thin progress
bar, `N/Period bars seeded` — instead of a value; there's nothing to display yet, and "—" alone
would read as broken rather than as "still warming up".

Status badge reuses the exact same four colors as everywhere else on this page (`status-green` /
`-amber` / `-red` / `-pending`, freshness-of-latest-arrival via `ComputeStatus`) but **different
label text** (`INDICATOR_STATUS_LABELS`, not `STATUS_LABELS`) — "On Track"/"Behind" reads fine for
a bar-count progress card, not for "is this indicator's value current"; "Live"/"Delayed"/"Stale"/
"Seeding…" do. Same colors, different words, because the *meaning* of green is still "current",
just phrased for what's actually being judged.

Supertrend gets an extra `Up`/`Down` direction badge (green/red respectively, reusing
`status-green`/`status-red`) next to the freshness badge — the two are independent facts (a
Supertrend can be solidly "Down" and still be stale, or "Up" and perfectly live), so they're two
separate badges, not one conflated indicator.

## Checklist

- [ ] Rows stay full-width and stacked
- [ ] `bucketMap` built from the count SET's real members (`SetMembersAsync`), not just its length
      — a cardinality can't tell you *which* buckets arrived
- [ ] Bucket starts computed as `sessionOpen + i × timeframeMinutes` — matches
      `RunningBucket.FloorToBucketStart` exactly, or the map silently misaligns
- [ ] `bucketMapGradient()` stays run-length-encoded (stops per transition, not per bucket)
- [ ] `--bars` matches `expectedTotal`, so the tick mask period is the real bar count
- [ ] The `<760px` mask-dropped fallback survives
- [ ] Shortfall stated in text (`N behind`), not by colour alone
- [ ] Timestamps through `clockTime()`
- [ ] Arrived/missing/pending stay fixed colors (`--green`/`--red`/`--border`) — never re-add a
      status- or phase-driven override; a bucket's own state doesn't change with anything else
- [ ] `.rail-now` stays a sibling of `.rail-map`, never nested inside it — the mask would chop it
- [ ] Status badge stays based on freshness of the latest arrival (`latestAgeSeconds`), not a
      cumulative count/ratio/gap — anything cumulative can never recover from a permanent
      historical gap (an earlier outage) even once the pipeline is fully healthy again
- [ ] Instrument color via `instrumentColorVar()`'s first-seen assignment, never a hash (no
      collision guarantee) or a hardcoded ticker→color map
- [ ] `--tag-*` used for identity only, never repurposed as a status color
