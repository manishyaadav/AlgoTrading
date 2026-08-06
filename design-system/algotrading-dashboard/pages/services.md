# Page: Services & Connections (`index.html#services`)

> Per MASTER.md's override rule, the rules here **replace** the Master file where they conflict.
> Everything not mentioned still follows MASTER.md.

**Role:** show every container in the compose project, grouped, with its health — and make the
`depends_on` graph between them readable. Both the grouping arrows and the health come from live
Docker data; nothing on this page is hand-drawn.

---

## Category panels

The three groups (Infrastructure / Helpers / Core Services) are a hand-maintained map in
`app.js` → `CATEGORIES`, because Docker has no notion of the grouping. Anything not listed falls
through to Core Services.

**Panels have no identity colour.** The old scheme gave each group a raw hex accent
(`#3b82f6` / `#f59e0b` / `#8b5cf6`), which violated MASTER.md's no-raw-hex rule and collided with
the console palette. The panel's top rule now reports the group's **health** instead:

| State | Top rule | Count |
|---|---|---|
| every container running | `--green` | `<b>6</b>/6 up` in `--green` |
| any container not running | `--red` | `<b>5</b>/6 up` in `--red` |

Structure carrying information, not decoration — a group losing a container is visible without
opening it. The count text carries the same fact as the colour, so status is never colour-alone.

## Service cards

```
⚡ country-live        ← Archivo wdth 106 / wght 620, 12.5px; icon in --accent
● Up 5 hours           ← --green/--red/--yellow dot + Fira Code 11px
8093→80                ← Fira Code 11px, --muted
↓ 1 dep · ↑ 4 dependents   ← Fira Code 10.5px, --accent, only while focused
```

Cards are `tabindex="0"` divs. They get an explicit `:focus-visible` ring because the global
focus rule only covers `a, button, input, select`.

## The interaction: dependency focus

Pointing at (or keyboard-focusing) a service lights **only the arrows touching it** and recedes
everything else — its `depends_on` targets and everything that depends on it.

| Element | Focused | Related | Unrelated |
|---|---|---|---|
| `.connection` | `--accent`, 2px, opacity 1 | — | opacity 0.1 |
| `.card` | lift 2px, accent border | accent border at 40%, `--panel-2` | opacity 0.4 |
| `.links` | opacity 1 | hidden | hidden |

Driven by `focusService()` in `app.js`, which reads `lastServices` — the same live payload the
arrows are drawn from. Edges carry `data-from` / `data-to` so the highlight can find them.
Handlers are delegated off `#services` so they survive a rebuild of the panels.

## Two constraints that are easy to break

1. **Card height must not change on hover.** The arrows are drawn from measured card positions,
   so revealing `.links` on hover by toggling `display` would reflow the grid and desync every
   line. `.links` and the ports row are always in the markup; only `opacity` animates. Rows that
   are genuinely empty are dropped with `:empty`, which is static per service and therefore safe.

2. **Don't rebuild the panels on the 5s poll.** `loadServices()` compares a signature of the
   container set and only re-renders `innerHTML` when that set actually changes; otherwise it
   updates each card's dot, status, ports and links in place. Rebuilding would cancel the hover,
   drop keyboard focus, and wipe the highlight mid-inspection — the same reason the console
   builds its route cards once.

## Checklist for changes here

- [ ] No raw hex — route through MASTER.md's tokens
- [ ] Status conveyed by colour **and** text (the `N/M up` count, the dot's neighbouring label)
- [ ] Anything added to a card is height-stable across hover
- [ ] New live values use Fira Code with `tabular-nums`
- [ ] Contrast checked in light **and** dark independently
- [ ] `prefers-reduced-motion` drops the hover lift (transform only; colour changes stay)
