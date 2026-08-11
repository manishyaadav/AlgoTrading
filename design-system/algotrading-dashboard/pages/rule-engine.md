# Page: Rule Engine (`index.html#rule-engine`)

> Per MASTER.md's override rule, the rules here **replace** the Master file where they conflict.

**Role:** show how a deployed strategy's rule tree would evaluate right now, using whatever live
data actually backs each condition — not a trading engine, a window onto one. See
`WARMUP_AND_INDICATOR_PLAN.md` section 4 ("Strategy execution engine") and
`StrategyService/README.md` for the backend side; this doc is the visual design only.

---

## Design brief: honor the app, don't reskin it

This page shipped from a static mockup reviewed and approved before any backend code existed (see
`WARMUP_AND_INDICATOR_PLAN.md`'s commit history for that conversation) — the brief was explicit:
reuse the existing design system exactly, don't invent a new visual identity for one page. Every
color token, every type role (Archivo for names/titles, Fira Code for values/data, Inter Tight for
body copy), and the card shell itself (`.rule-node` is `.candle-row`/`.status-card`'s same
`--surface`/`--border`/`backdrop-filter: blur(6px)` shape under a new name) come straight from
`style.css`'s existing tokens. The only genuinely new primitives are the vertical connector spine,
the headline-value/eyebrow layout, and the entry/exit fork — the things this page needed that
nothing else on the dashboard already had a pattern for.

## Layout: a spine, not a grid

Gates 1-4 (Deployed → Session → Trading Session Rules → Position) run down a single vertical
`.flow`, each `.rule-node` connected to the next by a `.connector` — a 2px line with a small arrow,
colored to the gate's own status (green on pass, red on fail, the neutral `--line` token while
still gray/unknown). This is deliberately a spine, not a branching diagram, for the first four
gates — they're a strict sequence, not a decision tree, so drawing them as one is honest about the
actual control flow (nothing skips a gate; a `fail` upstream doesn't currently grey out what's
below it either — see "What this page doesn't do yet" below).

**The fork happens once, at the Position gate** — `.fork` (`display:grid;
grid-template-columns:1fr 1fr`) splits into the "not in position" column (Entry Rules, live) and
the "in position" column (Exit/Stop-Loss Rules, a static preview). Two `.fork-col`s under one
`.fork-label` row each, not a re-branching tree per rule — the strategy schema only actually
branches once (in-position or not), so the visual only branches once too.

**Long and Short are two full copies of the same shape, stacked**, not tabbed or toggled — `.rule-
side` + `.rule-side-label` heading, each with its own complete fork. Considered a tab switcher;
rejected it, because the whole point of this page is "what would the rules currently decide,
seeing everything at once" — hiding Short behind a click while looking at Long defeats that, and
the two sides are short enough (2 entry rules, 2 risk rules, 5 exit rules each in the real deployed
strategy) that stacking costs a scroll, not real cognitive load.

## The source rail: what the engine is actually looking at

The rule tree answers "what would be decided". It structurally cannot answer the question that
comes first — **what is the engine reading, and is any of it real right now.** That's the rail: a
sticky left column listing every live input the evaluator touches, with its current value, the
scope it belongs to, and a count of the rules and gates reading it.

- **Backed vs. unbacked is the rail's primary distinction**, and unbacked cards get the same dashed
  border and transparent background `.placeholder-node` already uses for the gates this page can't
  answer. An input with no feed is the same kind of honest nothing as a gate with no answer, so it
  looks the same.
- **The count is on every card, backed or not.** `Position / order state` showing `1` next to a
  dashed border, wired to a gate that in turn governs an entire branch, states the stack's biggest
  gap as a structural fact rather than a paragraph of caveat.
- **A card shows the *input*, not the rule's use of it.** The rule row for the session gate renders
  `0.0038 × 24,583.80 = 93.42`; the card renders `24,583.80`, because 93.42 is a number the rule
  computes and exists nowhere in Redis. Captioning a card with a derived value would be labelling
  the wrong thing.
- **Sorted backed-first, then in the order the tree first reaches them** — so the rail reads roughly
  top-to-bottom alongside the rules beside it, and what's live is what you see first.

## Linking: hover to trace, click to pin

Hovering either side highlights the other and draws bezier connectors between them; everything
unrelated dims to 25% rather than hiding, so the shape of the whole tree stays legible and you can
see *where* the highlighted rules sit inside it.

- **Pinning exists because hover cannot survive scrolling.** The tree is several viewports tall; a
  hover-only interaction could never trace an input down to the rules at the bottom, which is the
  main thing anyone would want to do with it. Click pins, click again releases, and the connectors
  redraw on scroll (rAF-throttled).
- **Connectors are drawn only to rows currently on screen.** A curve to something 3000px below is
  noise, not information — the dimming already carries the relationship for everything off-screen.
- **The 44px column gap is a connector channel, not decoration.** At the 20px it started with, a
  curve to a rule 400px down had so little horizontal room it rendered as a near-vertical hairline
  against the column edge, indistinguishable from a border. Lines need room across to read as lines.
- **Below 1200px the rail moves above the tree and the connectors turn off.** The rail plus channel
  eat enough width that the fork columns stop being readable, and a legible tree matters more than
  the drawing. Highlighting still works in that mode; only the curves go.

**Never-evaluated rules carry their inputs too.** This is the whole reason the rail is worth
building: pinning the live Supertrend lights up two Long entry rules *and* two Exit rules that can
never run. "This rule reads Supertrend" is a fact about the rule definition — true whether or not
anything evaluates it — so naming it is honest, while resolving a value for it would not be. The
backend keeps those separate on purpose (`SourceRegistry.Touch` vs `Fill`): an input can only
become "backed" by actually having been read, never by being referenced.

## The rule row: an outcome with its receipts

A rule row is not a sentence with a verdict stapled on — it's a comparison you can check. Three
parts, matching the `1fr auto 1fr` shape the Strategy page's rule *editor* already uses for the
same three parts, so a rule reads the same way whether you're writing it or watching it run:

```
Supertrend (P20, x4, 5 Minutes, Nifty_Index_Spot)
24,480.34 (Down→RED)
    [ > ]                                    AND   Fail   ▾
EMA (P550, 5 Minutes, Nifty_Index_Spot)
24,514.07
Δ 33.73   33.73 away from passing   ▬▬▬▬▬▬▬▬   2.26× ATR
```

- **The live value sits directly under the operand it resolved from** (`.cmp-value`, Fira Code,
  tabular). A Literal renders its name once and labels itself `literal` in the muted voice instead
  of repeating itself — "GREEN / GREEN" would read like two independent facts agreeing.
- **The operator carries the outcome tint, not the operand values.** In `A > B` neither side is
  individually right or wrong; the comparison between them is. Tinting one value green would be
  asserting something the data doesn't say.
- **The gap readout is the point of the whole page.** "Fail" tells you the rule doesn't hold; `Δ
  33.73` tells you it's about to. Only drawn when both sides genuinely resolved to numbers — a text
  comparison (`Supertrend == GREEN`) has no meaningful distance, and neither does an unresolved
  side.
- **The bar is scaled to ATR and says so.** This is the one honesty constraint that shaped the
  design: a bar needs a scale, and 33.73 points is only "close" or "far" relative to something.
  ATR is the one real volatility unit available (it's already on the Supertrend hash), so the track
  runs 0–3× ATR and labels the multiple. **When no ATR is available there is no honest scale, so
  there is no bar** — the numbers show alone rather than a bar implying a range nobody defined.

## The evidence drawer: lineage, not a tooltip

`▾` on each row opens `.evidence` — for each side: the **exact Redis key** read, the **bar window**
the value belongs to, and the **raw hash fields** it was derived from, verbatim and unrounded. This
is the page's answer to "why does it think RED", and it deliberately distinguishes three different
kinds of "why":

| What you see | What it means |
|---|---|
| `redis  Indicator:Running:…` | Read from live data — here's exactly where |
| `from the rule definition — not live data` | A literal. There is no source because there's nothing to fetch |
| `looked in  Indicator:Running:…` | We know where this lives, checked, and it wasn't there / wasn't seeded |
| `no source in this stack yet` | Nothing anywhere backs this operand |

The one synthetic field is Supertrend's `band used` — which of the two stored bands *is* the
Supertrend line depends on `TrendDirection`, and that derivation is invisible in the raw hash.
Everything else is copied straight out of Redis; rounding it here would defeat the point.

**Never-evaluated rules get no drawer and keep their original compact one-line form.** Giving the
Exit/Risk branches the resolved-value anatomy would mean a `—` under every operand: three lines of
blank where there was one line of rule, and a static preview column taller than the live one beside
it. The visual gap between a live row and a preview row is now itself the signal.

## Interaction has to survive the refresh

The page refreshes every 5s. It used to rebuild this entire subtree on every tick, which made
sustained interaction impossible — an open drawer, a text selection, even a hover survived at most
five seconds. Two rules now:

1. **An unchanged payload touches the DOM not at all.** Indicators only actually move on a bar
   close (5 minutes for the deployed strategy), so the overwhelming majority of ticks are identical
   and are skipped by comparing the serialized response.
2. **A changed payload rebuilds, then restores.** Open drawers are tracked by a stable
   `scope:sequence` key (`long:entry:2`) in `ruleEngineOpenRows` and reopened after the rebuild.

Any future interactive state on this page has to answer the same question before it ships.

## Naming: `.eval-row`, not `.rule-row`

The Strategy page's rule *editor* already owns `.rule-row`. The first version of this page defined
`.rule-row` again, later in the same stylesheet — which silently restyled the editor's rows on the
other page. Two different components get two different names; check for an existing owner before
reusing a class name here.

## Status vocabulary: Pass/Fail/Unknown, not On-Track/Behind/Pending

Same three semantic colors as the rest of the app (`--green`/`--red`/`--muted`, via
`.badge.status-pass/fail/unknown`), **deliberately different words** from the Data page's
`STATUS_LABELS` (`On Track`/`Behind`/`Pending`). Those words answer "is this data flowing on
schedule"; this page answers "does this condition currently hold." Same underlying palette
decision (green = good, red = bad, muted = nothing to say yet), because the *meaning* of green
hasn't changed — just what's being judged. See `RULE_STATUS_LABELS` in `app.js`, kept as its own
constant rather than overloading `STATUS_LABELS`.

## The "Unknown" state is load-bearing, not a fallback

This is the one thing this page cannot compromise on: **`Unknown` must never be dressed up as a
guess.** The backend (`StrategyService/Engine/RuleEvaluator.cs`) resolves what it genuinely can —
Supertrend/EMA comparisons, the Pivot Central Range session-rule gate — and marks everything else
`unknown` with a plain-English `reason` (`"not evaluated — depends on state this stack doesn't
track yet"`), which the frontend renders as `.unwired-tag`, an *italic, muted, small* label — never
a colored badge, never anything that could be mistaken for a real answer at a glance. This mirrors
the Data page's "widgets without a value have no source in this stack yet — nothing here is filled
with placeholder numbers" ethos exactly, applied to boolean conditions instead of numeric widgets.

The Position gate (`.rule-node.placeholder-node`) gets the strongest version of this treatment —
**dashed border, muted title** — because it's not just one unresolved value, it's the gate that
decides which entire branch below it is real. The Exit/Stop-Loss column inherits the same
treatment for the same reason: it's not "this rule is unknown," it's "this whole branch never
actually runs yet," which needed to read as visually distinct from an individual unresolved
condition sitting inside an otherwise-live group (e.g. the Risk Management rules, which sit inside
a *live* Entry Rules card but are themselves each individually tagged unknown).

## What this page doesn't do yet (by design, not oversight)

- **No cascading dim.** A `fail` on Gate 2 (no session today) doesn't currently grey out gates 3-4
  or the forks below — every gate always renders its own real status against real data,
  independent of what's above it. Worth revisiting once this page has been lived with — a
  Holiday/Weekend day showing a fully "evaluated" Entry Rules card below a failed session gate
  could read as misleading. Deferred rather than guessed at, same as everything else on this page.
- **No manual position toggle.** Considered (see the design-discussion history in
  `WARMUP_AND_INDICATOR_PLAN.md`) and explicitly rejected for v1 in favor of the honest permanent
  placeholder — a fake toggle would let you "see" the Exit branch light up without it meaning
  anything, which is exactly the kind of thing this page exists to avoid.
- **No strategy picker.** Only one strategy is ever deployed today, so `loadRuleEngine()` just
  takes the first one `/api/strategies` reports as deployed. Add a picker when a second one
  actually gets deployed, not before — no UI for a problem that doesn't exist yet.

## Checklist

- [ ] Every color/type token comes from `style.css`'s existing `:root` set — no new hex values
- [ ] `Unknown` renders as `.unwired-tag` (italic, muted, small) — never a colored badge
- [ ] The Position gate and the Exit/Stop-Loss branch both carry `.placeholder-node` (dashed
      border) — the two places this page is honest about having nothing real to show
- [ ] Rule text goes through the Strategy page's existing `describeRule()`/`describeOperand()` —
      never reformatted a second way in this page's own code
- [ ] Long and Short both render in full, always, never behind a tab/toggle
- [ ] `RULE_STATUS_LABELS` stays separate from `STATUS_LABELS` — same colors, different words, on
      purpose
- [ ] The gap bar is only drawn against a **named** scale (ATR today). No scale → no bar
- [ ] Evidence `fields` are verbatim Redis values — never rounded or reformatted for display
- [ ] Never-evaluated rules keep the compact one-line form and get no drawer
- [ ] New interactive state survives the 5s refresh (skip-if-unchanged + restore-after-rebuild)
- [ ] Rows sized with a **container** query, not a media query — the same row renders full-width
      and inside a ~420px fork column
- [ ] Unbacked source cards use `.placeholder-node`'s dashed treatment — same honesty, same look
- [ ] A source card shows the input's own value, never a value the rule derives from it
- [ ] `FeedsRules` counts never-evaluated rules too — a dead branch reading a live input is the
      relationship the rail exists to show
- [ ] An input becomes `backed` only by being read (`Fill`), never by being referenced (`Touch`)
