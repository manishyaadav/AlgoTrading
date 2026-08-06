# Design System Master File

> **LOGIC:** When building a specific page, first check `design-system/pages/[page-name].md`.
> If that file exists, its rules **override** this Master file.
> If not, strictly follow the rules below.

---

**Project:** AlgoTrading Dashboard
**Generated:** 2026-08-06 (hand-authored from existing implementation + ui-ux-pro-max additions)
**Category:** Real-Time Monitoring / Data-Dense Dashboard
**Source of truth for colors/theme:** `EventDrivenSystem/Source/DashboardService/wwwroot/style.css` — this file documents and extends it, it does not replace it.

**Why not auto-generated as-is:** ui-ux-pro-max's `--design-system` search kept selecting "Dark Mode (OLED)" (light-mode unsupported) with a generic amber/indigo palette, regardless of query phrasing. This app already ships a working light+dark toggle with semantic green/red status colors (gains/losses, fresh/stale, running/exited) that the generic output would have destroyed. The tokens below are the app's real, working values; only the additions below (spacing scale, shadows, checklist) come from the skill.

---

## Global Rules

### Color Palette (existing — `:root` / `[data-theme]` in style.css)

Realigned to `wwwroot/home.css` so the console and the dashboard read as one app.

| Role | Dark | Light | CSS Variable | Usage |
|------|------|-------|--------------|-------|
| Background | `#080b12` | `#f1f4f9` | `--bg` | Page background |
| Panel | `#0e1320` | `#ffffff` | `--panel` | Cards, sidebar items, table zebra |
| Panel raised | `#141b2b` | `#e8edf5` | `--panel-2` | Hover surfaces |
| Border | `#1e2839` | `#d2d9e6` | `--border` | Card/panel borders, dividers |
| Text | `#e9eef7` | `#101725` | `--text` | Primary text |
| Muted | `#78859c` | `#566275` | `--muted` | Secondary text, labels, hints |
| Accent (brand) | `#7c89ff` | `#4149cf` | `--accent` / `--accent-bg` | Interactive: active nav, focus rings, links, hover borders |
| Green (positive) | `#38d9a4` | `#087550` | `--green` / `--green-bg` | Running, fresh, gains |
| Red (negative) | `#ff5c72` | `#c62038` | `--red` / `--red-bg` | Exited/dead, stale, losses |
| Yellow (caution) | `#ffb44a` | `#9a5906` | `--yellow` / `--yellow-bg` | "Other"/in-between states |
| Tag 1 (cyan) | `#22d3ee` | `#0e7490` | `--tag-1` | Categorical identity only — never status |
| Tag 2 (violet) | `#a78bfa` | `#6d28d9` | `--tag-2` | Categorical identity only — never status |
| Tag 3 (pink) | `#f472b6` | `#be185d` | `--tag-3` | Categorical identity only — never status |
| Tag 4 (sky) | `#38bdf8` | `#0369a1` | `--tag-4` | Categorical identity only — never status |
| Tag 5 (teal) | `#2dd4bf` | `#0f766e` | `--tag-5` | Categorical identity only — never status |

**`--tag-*` distinguishes same-type dynamic items** (currently: instrument/ticker names on the
Data page) **by identity, never by state.** Assign it in first-discovery order, cached for the
page's lifetime — see `instrumentColorVar()` in `app.js` — not by hashing the name (no collision
guarantee — two real tickers landed on the same tag under a plain hash) and never by a
hand-maintained name→color map; that's the exact mistake the old Services page `CATEGORIES` color
scheme made; see `pages/services.md`.

**`*-bg` tints are 0.15 alpha in dark, 0.08 in light.** Pills and badges put text on a tint of
its own hue, which costs ~0.5 of a contrast ratio; at 10px that's the difference between passing
and failing AA. Always re-measure a tinted chip **composited over its surface**, never the raw
token against the surface — see `pages/strategy.md`.
| Line | `#35435c` | `#b9c2d4` | `--line` | Connection graph edges |

### Two accents: `--phase` and `--accent`

Both files define both. Keeping them apart is what lets the whole app warm up during market
hours without breaking the controls.

| Token | Moves with | Carries | Used by |
|---|---|---|---|
| `--phase` | the trading day | atmosphere, session state | page wash, mark glyph, section icon, console eyebrow / rail fill / now-rule / current gate |
| `--accent` | nothing — fixed indigo | interaction | focus rings, active nav, hover borders, links, `.btn-primary`, card icons |

`--phase` maps from `data-phase` on `<html>`, set by `applyPhase()` (dashboard) and `render()`
(console), from the same country + exchange + services payloads:

| `data-phase` | When | `--phase` |
|---|---|---|
| `pre` | before 09:15 IST on a Normal day | `--accent` / indigo |
| `open`, `late` | during the session | `--yellow` / saffron |
| `closed`, `off` | after the close, holiday, weekend, day not gated | `--muted` |
| `down` | any container not `running` | `--red` |

**Never key a control to `--phase`.** An earlier version made the console's focus ring
phase-driven, so the ring turned low-contrast grey the moment the session closed. Controls need a
stable, always-legible colour.

`--accent` is **brand/interactive only, never a status**. Status stays green/red/yellow so a
colour never means two things. The light values for accent, green and yellow are darker than
their dark-theme counterparts on purpose — they carry 10–12px text and need 4.5:1 on a light
canvas. Don't "fix" them back to match.

**Do not introduce new raw hex values in components.** Everything routes through these tokens so
theme switching stays free. (The Services page's old `--cat-color` scheme — three raw hex
category colours — was removed for this reason; see `pages/services.md`.)

### Typography

Three roles, shared with `home.css`. All three load from one Google Fonts request in `index.html`.

| Role | Face | Setting | Where |
|------|------|---------|-------|
| Display | Archivo (variable) | `wdth 106–112, wght 620–750` | The mark, category headers, service/card names |
| Body | Inter Tight | 400/500 | Prose, hints, table cells, form fields |
| Data & labels | Fira Code | 400–600, `tabular-nums` | Every number, timestamp, badge, pill, `th`, and small-caps label |

- **Section headings (`h2`) are the page title:** Archivo, `wdth 112 / wght 700`, 28px,
  `-0.025em`, `--text`. One piece of display type per page. Before this the largest text on any
  dashboard page was the 18px header mark and every page opened with a 13px monospace caption —
  no voice at all. The eyebrow role sits below, on `.rules-group-label` / `.sub-section-label`.
- **Prose is capped at `74ch`** (`.hint`) with `line-height: 1.6`. Body copy is 16px/1.5 with
  `-webkit-font-smoothing: antialiased`, matching `home.css` — without these the same typeface
  renders visibly heavier and tighter than on the console.
- **Anything live and numeric gets `tabular-nums`** — values update every 5s and proportional
  figures make columns jitter as digit counts change.
- Reach for Archivo only where a name needs weight. Prose stays Inter Tight; a page set mostly in
  Archivo loses the contrast that makes the display face mean anything.

### Spacing Scale (addition — matches the app's existing dense rhythm)

The app already uses 8/12/14/16/24/32px spacing ad hoc and consistently. Formalizing it as tokens (safe to add to `:root`, doesn't change any rendered value):

```css
--space-2xs: 4px;   /* icon-to-label gaps, .dot spacing */
--space-xs:  8px;   /* table cell padding, .cards-mini gap */
--space-sm:  12px;  /* card padding, section-head gaps */
--space-md:  16px;  /* header padding, category-row gap */
--space-lg:  24px;  /* main content padding, section margin-bottom */
--space-xl:  32px;  /* section margin-bottom (large), placeholder padding */
```

### Shadow Depths (addition — not currently used; app relies on borders only)

Optional, for cases where border-only separation isn't enough (e.g. dropdowns, the strategy panel, modals if added):

```css
--shadow-sm: 0 1px 2px rgba(0,0,0,0.12);
--shadow-md: 0 4px 10px rgba(0,0,0,0.18);
--shadow-lg: 0 10px 24px rgba(0,0,0,0.24);
```

Keep these subtle — the current design leans on `--border` + `--panel` contrast, not elevation, and that's consistent with the "Real-Time Monitoring / Data-Dense Dashboard" style (flat, high information density, minimal chrome).

### Surfaces

Top-level panels use `--surface` (`--panel` at 78%) with `backdrop-filter: blur(6px)` and a
**12px** radius, so they pick up the page wash the way the console's `.session` does. That's
`.category-panel`, `.exchange-row`, `.candle-row`, `.strategy-row`, `.status-card`,
`.strategy-panel`, `.placeholder`.

Nested tiles — the `.card` service tiles inside `.category-panel` — stay **opaque `--panel` at
8px**. A nested element sharing its container's radius and translucency stops reading as nested.

### The page wash — do not strengthen it

`body::before` is a 60vh radial gradient of `--phase` at **9%**. It is capped there for a
measured reason: `--muted` body copy sits directly on this background, and at 13% the wash lifted
it to **4.26:1** in dark/open, under AA. 9% holds the worst case at 4.63. Re-measure before
raising it.

### Border Radius (existing, for reference — already consistent)

`6px` (icon buttons, nav items), `8px` (cards), `10px` (category panels, placeholders). No change recommended — keep new components on this scale.

---

## Motion (addition)

Nothing currently animates except the 0.15s bg/color theme-switch transition. If adding entrance motion (e.g. status rows appearing after a fetch), keep it subtle to match the "Real-Time Monitoring" style — this UI reads as instrumentation, not marketing:

```css
--motion-fast: 150ms ease;
--motion-standard: 200ms ease;
```

- ✅ Fade/slide small distances (4-8px) on new data rows
- ✅ Respect `prefers-reduced-motion`
- ❌ Avoid bouncy/overshoot easing (`back.out`, spring) — reads as playful, wrong register for a monitoring tool
- ❌ Don't animate the status dots' color changes with anything but the theme-switch transition already in place

---

## Anti-Patterns (Do NOT Use)

- ❌ Raw hex colors in component CSS — always go through the existing `--bg/--panel/--border/--text/--muted/--green/--red/--yellow/--line` tokens
- ❌ Conveying status by color alone — the app already pairs color with the `.dot` + label text; keep that pattern for any new status UI
- ❌ Emojis as icons — this app uses inline SVG (`data-icon` + icon system); stay consistent with it
- ❌ Bouncy/overshoot motion easing (see above)
- ❌ New elevation/shadow-heavy components — stay flat and border-driven

---

## Pre-Delivery Checklist

- [ ] New colors reuse existing `--*` tokens (light AND dark values checked)
- [ ] Status conveyed via color + text/icon, not color alone
- [ ] Cursor:pointer on new clickable elements (existing `.icon-btn`, `.nav-item` already do this)
- [ ] Hover states use existing transition timing (150ms)
- [ ] Numeric/data columns use tabular figures if using the Fira Code addition
- [ ] Responsive check: sidebar collapses to horizontal scroll below 700px (existing breakpoint) — verify new content follows the same rule
- [ ] `prefers-reduced-motion` respected for any new animation
- [ ] Contrast checked independently in both light and dark (don't assume one theme covers the other)
