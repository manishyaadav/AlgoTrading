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
