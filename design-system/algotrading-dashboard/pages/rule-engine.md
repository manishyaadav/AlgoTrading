# Page: Rule Engine (`index.html#rule-engine`)

> Per MASTER.md's override rule, the rules here **replace** the Master file where they conflict.

**Role:** show how a deployed strategy's rule tree would evaluate right now, using whatever live
data actually backs each condition — not a trading engine, a window onto one. See
`WARMUP_AND_INDICATOR_PLAN.md` section 4 ("Strategy execution engine") and
`StrategyService/README.md` for the backend side; this doc is the visual design only.

---

## This is the second design, not the first

The page shipped originally as an always-show-everything spine: every gate (deployed? → session? →
session rules → position), both sides (Long and Short stacked in full), and both branches per side
(Entry live, Exit a static preview) all on screen simultaneously, plus an interactive source rail
with hover-to-trace linking and a per-rule evidence drawer showing the exact Redis key and raw hash
fields behind every value. That version's full rationale — the gap bar's ATR scaling, the
ownership-avoids-name-collision story behind `.eval-row`, the ~420px container-query row, the ATR
gap-bar honesty constraint — is preserved in this file's git history if any of it needs revisiting.

It was replaced with a **single-branch "Focus" redesign**, built from a mockup: pick a side
(Long/Short) and a lens (Entry/Exit/Risk), and only that one chain renders. The trade-off is real,
not free — the old page could answer "what does the whole tree look like right now" in one glance;
this one answers "is *this* path armed" faster, at the cost of the other seven combinations being a
click away. The interactive evidence-drawer/hover-linking system did not carry over — replaced by a
plain "Reads" strip at the bottom of whatever's currently on screen, no drill-down. That's a
genuine capability drop, made deliberately: the mockup this shipped from didn't have room for it,
and re-adding it to the new layout is a future call, not an oversight to silently restore.

## Above the branch: what's common to every strategy or every gate, shown once

Two things are facts about the *whole page*, not about whichever branch is currently focused, and
render outside the toggle:

- **The strategy switcher** (`.strategy-switcher`, a row of `.strategy-chip` buttons) only renders
  when 2+ strategies are deployed at once. Clicking a chip re-fetches and repaints everything below
  it for that strategy; the currently-selected side/lens (Long/Short, Entry/Exit/Risk) is *not*
  reset by a strategy switch — `ruleEngineSide`/`ruleEngineTab` are module state independent of
  `ruleEngineSelectedId`, so checking "Short/Exit" across several strategies in a row doesn't mean
  re-clicking the same two toggles every time.
- **"Strategy deployed?" doesn't exist as a gate at all.** The page only ever reaches a strategy's
  tree through one that's already known to be deployed (`GET .../rule-status` 404s otherwise, and
  the switcher is built off `deployedVersion`) — a gate whose answer is always "yes" had nothing to
  tell you, so it was never carried into this design either.
- **The session/holiday gate is a `GET /api/session-status` call**, independent of which strategy is
  selected (Redis `"India"` is the same state for all of them). It's folded into the **Gates
  Cleared** checklist as that checklist's first item, not rendered as its own separate block —
  see below.

## Layout: a checklist rail + one focused panel

`.focus-layout` is a fixed 260px rail (`.gates-rail`) beside a fluid main panel (`.focus-main`), not
sticky — the page is short enough now (one branch, not the whole tree) that a sticky rail would add
scroll-jank for no reason the old page's several-viewports-tall tree actually had.

### Gates Cleared: four items, only one of which is a gate

`.gates-list` renders, in order:

1. **Session today** — the standalone session gate above, compact: `{state} · gated open` on pass,
   `{state} — no session today` on fail.
2. **Trading session rules** — the strategy's own session-gate rule group (Pivot Central Range vs.
   prior close, in the shipped strategy). Compact detail reuses the exact same gap-phrase function
   the spine uses (`ruleGapPhrase`), so "passing by 113.89" in the checklist and in the full rule
   row downstream always agree — computed once, displayed twice, never two independent formatters
   that could drift apart.
3. **In a position** — the permanent placeholder gate (see below). Fixed client-side copy ("Nothing
   tracks this — assumed flat"), since the backend's full sentence is written for a lone card, not a
   one-line checklist item; the *tone* (icon color) still comes from the live `positionGate.status`.
4. **`{Side}` branch** — not a gate at all, a "you are here" pointer to whichever side/lens the main
   panel currently shows. Gets its own icon tone (`.gate-icon.pointer`, accent-colored) specifically
   *so it's not mistaken for a passed gate* — an arrow, not a checkmark.

Icons are ✓ (pass, green), ✕ (fail, red), ? (unknown, muted), ↓ (pointer, accent) — `gateChecklistIcon()`
in `app.js`.

### Data health

Directly below the checklist, not a separate card: a thin progress bar (`{backed}/{total}` of
`data.sources`, the same union of every input the current strategy's rule tree touches this page
always had) plus a one-line caption naming what's *not* backed and why it matters
("`{n}` inputs have no writer. Exit and risk rules can't evaluate until they do."). This is the
compact replacement for the old page's `{backed} of {total} backed` rail header — the full
per-input card list that used to hang below it moved to the **Reads** strip (below), scoped to only
what's currently on screen rather than everything in the tree at once.

## The focus panel: side, lens, spine, reads

`.focus-toolbar` holds three things in a row: the side toggle, the lens toggle, and a right-aligned
"N of M met" pill.

- **Side toggle** (`.side-toggle`, Long/Short) — each button carries a `.side-dot` colored by that
  side's **live** favorability (`data.long.entryRules.status` / `data.short.entryRules.status`):
  green if its entry rules currently pass, red if they fail, muted if unknown. **Not** a fixed
  Long=green/Short=red brand color — a side reads red when its own condition currently doesn't hold,
  whichever side that is. A fixed color here would silently disagree with the badge one click away.
- **Lens toggle** (`.tab-toggle`, Entry/Exit/Risk) — see below for what each shows.
- **Met pill** (`.met-pill`) — only counts rules that actually resolved to pass/fail. A
  never-evaluated group (Risk Management on its own, the whole Exit lens) renders `Not evaluated`
  instead of a ratio; an unknown rule inside an otherwise-live group (see "Entry" below) is drawn on
  the spine but excluded from both the numerator and denominator — it's not a miss, it's a question
  this stack can't answer yet, and counting it against the ratio would misrepresent what "2 of 2
  met" is actually claiming.

**What each lens shows:**

| Lens | Content | Live? |
|---|---|---|
| **Entry** | Entry Rules (AND chain), **then** Risk Management Rules chained on with a `THEN` transition | Entry: live. Risk: never evaluated |
| **Exit** | `UpdateStopLossRules` + `ExitRules` combined (OR chain) | Never evaluated |
| **Risk** | Risk Management Rules alone | Never evaluated |

Entry deliberately shows more than its own name: whether an armed entry can actually be **sized**
(Risk Management) is part of "would this trade happen", not a separate question, so the two groups
share one spine with a `THEN` transition rather than living in different tabs — Risk gets its own
tab too, as a way to zoom into just the sizing rules in isolation, but Entry's own view tells the
complete "is this trade real" story on one screen. `combinedSpineForEntry()` in `app.js` is the one
place this THEN-stitching happens; every other lens is a single group's rules verbatim.

### The spine

One AND/OR/THEN chain, top to bottom, each rule a `.spine-node` with a colored `.spine-dot` on a
continuous vertical line (an `::before` per node, clipped so the line starts/ends at the first/last
dot rather than running past the panel edge) and a `.spine-link` row between nodes carrying the
actual AND/OR/THEN label.

Each node's body (`spineCompareHtml`) reads left to right the way the rule itself does —
`{leftName} {leftValue} {operator} {rightValue} {rightName}` — reusing `describeOperand()` (shared
with the Strategy page's rule editor) for the names. **A literal never shows its value twice**:
`describeOperand(Literal "RED")` already prints `"RED"`; showing the resolved value alongside it
too would print `"RED RED"`. Checked on the operand's own `type`, not the evidence's `kind` — a
never-evaluated rule's evidence is always `Kind: "unresolved"` even for a literal operand (nothing
gets resolved at all in that branch), which would otherwise draw a `—` placeholder implying a
literal is missing data it was never going to need.

Below the compare line, one of three things:

- **Unresolved** (`ev.status === "unknown"`) — the backend's plain-English reason, muted.
- **Numeric comparison** — the gap readout (`ruleGapHtml`, unchanged from the old design): `Δ 33.73`,
  a phrase (`"passing by 33.73"` / `"33.73 away from passing"`), and — only when both sides carry an
  `Atr` field (Supertrend does) — a bar scaled 0–3× ATR with the multiple labeled. No ATR, no bar;
  a bar needs a named scale or it's just implying a range nobody defined.
- **Text/equality comparison** (e.g. Supertrend color vs. a Literal) — `"Matched exactly"` /
  `"Did not match"` plus, when available, `"as of {HH:MM}"` from the operand's real `AsOf` bar
  timestamp. **Never** a fabricated "flipped at" moment — this stack only keeps the current running
  indicator state, not a change history, so there is no honest way to know when a value last
  changed, only what bar it's currently as-of. Say the true thing, not the more satisfying one.

### Reads strip

`.reads-strip`, bottom of the panel: every `data.sources` entry actually referenced by the rules
currently on screen (union of `sourceIds` across the visible spine), each a small label+value tile,
`"no source"` in italics if unbacked. Deliberately narrower than the old always-on source rail —
this is "what's behind exactly what you're looking at", not the full inventory across every
side/lens at once.

## The "Unknown"/"Can't evaluate" state is still load-bearing

Carried over unchanged in substance from the first design, renamed in label: **a rule this stack
can't evaluate must never be dressed up as a guess.** `RuleEvaluator.cs` resolves what it genuinely
can (Supertrend/EMA comparisons, the session-rule gate) and marks everything else `unknown` with a
plain-English reason; the frontend shows `Can't evaluate` (`RULE_STATUS_LABELS`, matching the
mockup's wording — was `Unknown` in the first design, same meaning) rather than any colored badge
that could read as a real answer at a glance. The permanent-placeholder Position gate and the whole
Exit/Risk-alone lenses inherit this the same way the old page's placeholder nodes did.

## What this page doesn't do (by design, not oversight)

- **No cascading dim.** A `fail` on the session gate, or on Trading Session Rules, doesn't currently
  grey out the focus panel below it — everything always renders its own real status against real
  data, independent of what's above it in the checklist. Same deferred-not-guessed stance as the
  first design.
- **No manual position toggle.** Explicitly rejected, same reasoning as before: a fake toggle would
  let you "see" the Exit lens light up without it meaning anything.
- **No evidence drawer / no hover-linking.** This is the one real capability the redesign dropped,
  not merely renamed — see "This is the second design, not the first" above. Re-adding a
  provenance drill-down to this layout (a modal? an expand-in-place on the Reads tile?) is an open
  question, not a planned next step.

## Checklist

- [ ] Every color/type token comes from `style.css`'s existing `:root` set — no new hex values
- [ ] `Can't evaluate` renders as `.badge.status-unknown` (muted, not colored red/green) — never a
      claim of a real pass/fail answer
- [ ] The Position gate's checklist item and the Exit/Risk lenses all read `Not evaluated`/muted,
      the two places this page is honest about having nothing real to show
- [ ] Rule text goes through the shared `describeOperand()` — never reformatted a second way here
- [ ] A literal operand's value renders once, checked on the operand's own `type`, never on
      `evidence.kind` (unreliable for never-evaluated rules — see the spine section above)
- [ ] `RULE_STATUS_LABELS` stays separate from the Data page's `STATUS_LABELS` — same colors,
      different words, on purpose
- [ ] The gap bar is only drawn against a **named** scale (ATR today). No scale → no bar
- [ ] No fabricated timestamps — `"as of {AsOf}"`, never `"flipped/changed at {time}"`, since this
      stack keeps no change history, only current state
- [ ] The "met" ratio excludes never-evaluated rules from both numerator and denominator
- [ ] Side-toggle dots reflect live favorability, never a fixed Long/Short brand color
- [ ] New interactive state (side/tab selection) survives the 5s refresh — see
      `renderRuleEngineContent()`/`ruleEngineLastData` in `app.js`
- [ ] The Reads strip only shows sources the *currently visible* spine actually references
