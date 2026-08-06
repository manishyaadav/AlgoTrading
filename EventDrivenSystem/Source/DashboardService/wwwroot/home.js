/* ============================================================================
   AlgoTrading — console entry page.
   Reads the same endpoints the dashboard reads; adds no server surface of its
   own. Every "now" is computed explicitly in IST, matching the backend's
   IstNow() discipline, so the page is correct from any viewer timezone.
   ========================================================================== */

const POLL_MS = 5000;
const STRATEGY_API_BASE = `${location.protocol}//${location.hostname}:8096`;

const SESSION_OPEN_MIN = 9 * 60 + 15;   // 09:15 IST
const SESSION_CLOSE_MIN = 15 * 60 + 30; // 15:30 IST
const SESSION_MINUTES = SESSION_CLOSE_MIN - SESSION_OPEN_MIN; // 375

// Order in which exchange-live fires its five fixed-time timers.
const STAGES = [
  { key: "Initiated", label: "Init" },
  { key: "PreOpened", label: "Pre-open" },
  { key: "Opened", label: "Open" },
  { key: "PreClosed", label: "Pre-close" },
  { key: "Closed", label: "Close" },
];

const ICONS = {
  graph: `<circle cx="6" cy="6" r="2.4"/><circle cx="18" cy="6" r="2.4"/><circle cx="12" cy="18" r="2.4"/><path d="M8 7l7-1M8.5 8l3 8M15.5 8l-3 8"/>`,
  freshness: `<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/>`,
  building: `<path d="M4 21h16M6 21V6.5L12 3l6 3.5V21"/><path d="M9.5 21v-5h5v5"/><path d="M9 9h1.4M13.6 9H15M9 13h1.4M13.6 13H15"/>`,
  stack: `<rect x="4" y="3.5" width="16" height="5" rx="1.3"/><rect x="4" y="10" width="16" height="5" rx="1.3"/><rect x="4" y="16.5" width="16" height="4.5" rx="1.3"/>`,
  target: `<circle cx="12" cy="12" r="8.5"/><circle cx="12" cy="12" r="4.8"/><circle cx="12" cy="12" r="1" fill="currentColor" stroke="none"/>`,
  sync: `<path d="M4.5 12a7.5 7.5 0 0 1 13-5.2M17.5 3.5v4.3h-4.3"/><path d="M19.5 12a7.5 7.5 0 0 1-13 5.2M6.5 20.5v-4.3h4.3"/>`,
  history: `<path d="M3.5 12a8.5 8.5 0 1 0 2.8-6.3"/><path d="M3.5 3.8v4.3h4.3"/><path d="M12 8v4.5l3 2"/>`,
  sliders: `<path d="M4 6h6M14 6h6M4 12h10M18 12h2M4 18h13"/><circle cx="12" cy="6" r="2"/><circle cx="16" cy="12" r="2"/><circle cx="19" cy="18" r="2"/>`,
  bell: `<path d="M6.5 8.5a5.5 5.5 0 0 1 11 0c0 4.5 1.8 5.8 1.8 5.8H4.7s1.8-1.3 1.8-5.8z"/><path d="M9.7 18a2.3 2.3 0 0 0 4.6 0"/>`,
  sun: `<circle cx="12" cy="12" r="4.5"/><path d="M12 2.5v3M12 18.5v3M4.5 12h-3M22.5 12h-3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1"/>`,
  moon: `<path d="M20 14.5A8.5 8.5 0 1 1 9.5 4a7 7 0 0 0 10.5 10.5z"/>`,
};

// Destinations, in the same order as the dashboard's left nav. `live: false`
// marks the pages that are still placeholders — the card says so rather than
// pretending to have a number.
const ROUTES = [
  { page: "services", name: "Services & Connections", icon: "graph", live: true },
  { page: "freshness", name: "Data Freshness", icon: "freshness", live: true },
  { page: "exchanges", name: "Exchanges", icon: "building", live: true },
  { page: "data", name: "Data", icon: "stack", live: true },
  { page: "strategy", name: "Strategy", icon: "target", live: true },
  { page: "datasync", name: "Data Sync", icon: "sync", live: false },
  { page: "backtest", name: "Backtest", icon: "history", live: false },
  { page: "broker", name: "Broker Configuration", icon: "sliders", live: false },
  { page: "alerts", name: "Alerts / Signals", icon: "bell", live: false },
];

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const svg = (key) => `<svg class="card__icon" viewBox="0 0 24 24" aria-hidden="true">${ICONS[key] || ""}</svg>`;
const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;

/* ── IST clock ───────────────────────────────────────────────────────────── */

const IST_FMT = new Intl.DateTimeFormat("en-GB", {
  timeZone: "Asia/Kolkata",
  weekday: "short", day: "2-digit", month: "short", year: "numeric",
  hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
});

function istParts() {
  const p = {};
  for (const { type, value } of IST_FMT.formatToParts(new Date())) p[type] = value;
  return p;
}

/** Minutes past midnight, IST. */
function istMinuteOfDay(p) {
  return Number(p.hour) * 60 + Number(p.minute);
}

/** Where the current instant sits on the 09:15→15:30 axis, as a percentage. */
function sessionPct(p) {
  const m = istMinuteOfDay(p);
  if (m <= SESSION_OPEN_MIN) return 0;
  if (m >= SESSION_CLOSE_MIN) return 100;
  return ((m - SESSION_OPEN_MIN) / SESSION_MINUTES) * 100;
}

function tickClock() {
  const p = istParts();
  $("stamp-date").textContent = `${p.weekday.toUpperCase()} ${p.day} ${p.month.toUpperCase()} ${p.year}`;
  $("stamp-time").textContent = `${p.hour}:${p.minute}:${p.second}`;

  const pct = sessionPct(p);
  $("tracks").style.setProperty("--now", `${pct}%`);
  $("now-flag").textContent = `${p.hour}:${p.minute}`;
}

/* ── Polling ─────────────────────────────────────────────────────────────── */

async function getJson(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

/** Resolves to the parsed body, or null if that endpoint is unreachable. */
const tryJson = (url) => getJson(url).then((v) => v, () => null);

async function poll() {
  const [services, country, exchanges, ingestion, aggregation, freshness, strategies] = await Promise.all([
    tryJson("/api/services"),
    tryJson("/api/country"),
    tryJson("/api/exchanges"),
    tryJson("/api/data-ingestion"),
    tryJson("/api/aggregation"),
    tryJson("/api/freshness"),
    tryJson(`${STRATEGY_API_BASE}/api/strategies`),
  ]);

  const state = { services, country, exchanges, ingestion, aggregation, freshness, strategies };
  render(state);

  const dead = [services, country, exchanges, ingestion, aggregation, freshness].filter((v) => v === null).length;
  const p = istParts();
  const pollEl = $("poll-state");
  pollEl.textContent = dead
    ? `${plural(dead, "endpoint", "endpoints")} unreachable`
    : `refreshed ${p.hour}:${p.minute}:${p.second}`;
  pollEl.classList.toggle("poll--error", dead > 0);
}

/* ── Derived facts ───────────────────────────────────────────────────────── */

function readServices(services) {
  if (!Array.isArray(services)) return { total: 0, up: 0, down: 0, known: false };
  const up = services.filter((s) => s.state === "running").length;
  return { total: services.length, up, down: services.length - up, known: true };
}

/** The NSE row if we have it, else whichever exchange reported first. */
function primaryExchange(exchanges) {
  if (!Array.isArray(exchanges) || !exchanges.length) return null;
  return exchanges.find((e) => /nse/i.test(e.exchangeName)) || exchanges[0];
}

/**
 * One of: off (holiday/weekend/unknown), pre, open, late, closed, down.
 * Service health outranks the clock — a dead stack isn't "flowing" whatever
 * time it is.
 */
function readPhase(state, svc) {
  if (svc.known && svc.down > 0) return "down";

  const c = state.country;
  if (!c || !c.found || !c.isToday) return "off";
  if (c.state && c.state !== "Normal") return "off";

  const ex = primaryExchange(state.exchanges);
  if (ex && ex.isToday && ex.state === "PreClosed") return "late";

  const m = istMinuteOfDay(istParts());
  if (m < SESSION_OPEN_MIN) return "pre";
  if (m >= SESSION_CLOSE_MIN) return "closed";
  return "open";
}

function behindCount(rows) {
  return Array.isArray(rows) ? rows.filter((r) => r.status === "amber" || r.status === "red").length : 0;
}

/* ── Render ──────────────────────────────────────────────────────────────── */

function render(state) {
  const svc = readServices(state.services);
  const phase = readPhase(state, svc);
  document.documentElement.setAttribute("data-phase", phase);

  renderVerdict(state, svc, phase);
  renderGates(state);
  renderTracks(state);
  renderRoutes(state, svc);
}

function renderVerdict(state, svc, phase) {
  const c = state.country;
  const ex = primaryExchange(state.exchanges);
  const behind = behindCount(state.ingestion) + behindCount(state.aggregation);
  const contracts = Array.isArray(state.ingestion) ? state.ingestion.length : 0;

  let label, headline;

  if (phase === "down") {
    label = "Service failure";
    headline = `${plural(svc.down, "service is", "services are")} down.`;
  } else if (phase === "off") {
    if (!c || !c.found) {
      label = "Day not gated yet";
      headline = "The day hasn’t been gated.";
    } else if (!c.isToday) {
      label = `Country state last set ${c.date || "—"}`;
      headline = "The day hasn’t been gated.";
    } else {
      label = `${c.state} · no session`;
      headline = "Markets are closed today.";
    }
  } else if (phase === "pre") {
    label = ex && ex.isToday ? `${c.state} day · ${stageLabel(ex.state)}` : `${c.state} day · standing by`;
    headline = "Standing by for the open.";
  } else if (phase === "closed") {
    label = `${c.state} day · session over`;
    headline = behind ? "The session ended short." : "Session complete.";
  } else {
    label = `Market open · ${c.state} day`;
    headline = behind ? "Ingestion is behind." : contracts ? "Everything is flowing." : "Open, but nothing is flowing.";
  }

  $("phase-label").textContent = label;
  $("verdict").textContent = headline;
  $("summary").innerHTML = summaryHtml(state, svc, contracts);
}

function stageLabel(key) {
  const s = STAGES.find((x) => x.key === key);
  return s ? s.label : key || "unknown";
}

function summaryHtml(state, svc, contracts) {
  const bits = [];

  bits.push(svc.known
    ? `<b>${svc.up} of ${svc.total}</b> ${svc.total === 1 ? "service" : "services"} up`
    : `<b>Docker</b> unreachable`);

  bits.push(`<b>${contracts}</b> ${contracts === 1 ? "contract" : "contracts"} ingesting`);

  if (Array.isArray(state.strategies)) {
    const deployed = state.strategies.filter((s) => s.deployedVersion).length;
    bits.push(state.strategies.length
      ? `<b>${deployed} of ${state.strategies.length}</b> strategies deployed`
      : `<b>no</b> strategies yet`);
  }

  if (Array.isArray(state.freshness)) {
    const stale = state.freshness.filter((f) => f.isStale).length;
    bits.push(stale ? `<b>${stale}</b> stale cache ${stale === 1 ? "key" : "keys"}` : `<b>no</b> stale cache keys`);
  }

  const c = state.country;
  if (c && c.found && c.holiday && c.holiday.reason) {
    bits.push(`today is <b>${esc(c.holiday.reason)}</b>`);
  }

  return bits.join(" &nbsp;·&nbsp; ");
}

function renderGates(state) {
  const ex = primaryExchange(state.exchanges);
  const currentIdx = ex && ex.isToday ? STAGES.findIndex((s) => s.key === ex.state) : -1;

  $("gates").innerHTML = STAGES.map((s, i) => {
    const cls = i < currentIdx ? "gate gate--passed" : i === currentIdx ? "gate gate--current" : "gate";
    return `<li class="${cls}">${esc(s.label)}</li>`;
  }).join("");
}

/**
 * The signature: one rail per contract per timeframe, all sharing the single
 * 09:15→15:30 axis and the one vertical "now" rule, so a contract falling
 * behind its neighbours is visible without reading a single number.
 */
function renderTracks(state) {
  const rows = [];
  for (const r of state.ingestion || []) rows.push({ ...r, thin: false });
  for (const r of state.aggregation || []) rows.push({ ...r, thin: true });
  rows.sort((a, b) => a.contract.localeCompare(b.contract) || a.timeframe - b.timeframe);

  const host = $("tracks");
  $("tracks-empty").hidden = rows.length > 0;
  $("now").hidden = rows.length === 0;

  // Rails are rebuilt only when the set of contracts changes. On an ordinary
  // poll the existing nodes are updated in place, so the fill slides to its new
  // width instead of jumping — the CSS transition needs the same element.
  const signature = rows.map((r) => `${r.contract}:${r.timeframe}`).join("|");
  if (host.dataset.signature !== signature) {
    host.dataset.signature = signature;
    host.querySelectorAll(".track").forEach((el) => el.remove());
    host.insertAdjacentHTML("beforeend", rows.map(trackSkeleton).join(""));
  }

  const rails = host.querySelectorAll(".track");
  rows.forEach((r, i) => updateTrack(rails[i], r));
}

function trackSkeleton(r) {
  return `
    <div class="track${r.thin ? " track--thin" : ""}">
      <div class="track__meta">
        <span><span class="track__name">${esc(r.contract)}</span><span class="track__tf">${r.timeframe}m</span></span>
        <span class="track__count"></span>
      </div>
      <div class="track__rail" role="img" style="--bars:${r.expectedTotal || 1}">
        <div class="layer layer--rest"></div>
        <div class="layer layer--gap"></div>
        <div class="layer layer--fill"></div>
      </div>
    </div>`;
}

function updateTrack(el, r) {
  if (!el) return;
  const total = r.expectedTotal || 1;
  const behind = r.status === "amber" || r.status === "red";
  const short = Math.max(0, r.expectedSoFar - r.count);

  el.classList.toggle("track--behind", behind);
  el.querySelector(".track__count").innerHTML =
    `<b>${r.count}</b> / ${r.expectedTotal} bars${behind && short ? ` · ${short} short` : ""}`;

  const rail = el.querySelector(".track__rail");
  rail.style.setProperty("--bars", total);
  rail.style.setProperty("--fill", `${Math.min(100, (r.count / total) * 100)}%`);
  rail.style.setProperty("--exp", `${Math.min(100, (r.expectedSoFar / total) * 100)}%`);
  rail.setAttribute(
    "aria-label",
    `${r.contract} ${r.timeframe} minute: ${r.count} of ${r.expectedTotal} bars ingested, ${r.expectedSoFar} expected by now`
  );
}

function renderRoutes(state, svc) {
  const stats = {
    services: svc.known
      ? { text: `<b>${svc.up}</b> of ${svc.total} running`, tone: svc.down ? "warn" : "ok" }
      : { text: "Docker unreachable", tone: "warn" },

    freshness: statFor(state.freshness, (f) => {
      const stale = f.filter((x) => x.isStale).length;
      return { text: `<b>${stale}</b> stale of ${f.length} keys`, tone: stale ? "warn" : "ok" };
    }),

    exchanges: statFor(state.exchanges, (e) => {
      const ex = primaryExchange(e);
      if (!ex) return { text: "No exchange has reported", tone: "warn" };
      return { text: `<b>${esc(ex.exchangeName)}</b> · ${esc(ex.isToday ? stageLabel(ex.state) : "not run today")}`, tone: ex.isToday ? "ok" : "warn" };
    }),

    data: statFor(state.ingestion, (rows) => {
      const behind = behindCount(rows) + behindCount(state.aggregation);
      if (!rows.length) return { text: "Nothing ingesting yet", tone: "" };
      return { text: `<b>${rows.length}</b> ${rows.length === 1 ? "contract" : "contracts"} · ${behind ? `${behind} behind` : "on pace"}`, tone: behind ? "warn" : "ok" };
    }),

    strategy: statFor(state.strategies, (s) => {
      const deployed = s.filter((x) => x.deployedVersion).length;
      return { text: `<b>${deployed}</b> of ${s.length} deployed`, tone: deployed ? "ok" : "" };
    }),
  };

  const host = $("routes");

  // Built once. Re-rendering the whole grid every poll would throw away keyboard
  // focus every five seconds, so only the stat line is rewritten afterwards.
  if (!host.childElementCount) {
    host.innerHTML = ROUTES.map((r) => `
      <a class="card${r.live ? "" : " card--idle"}" href="index.html#${r.page}" data-page="${r.page}">
        <span class="card__top">${svg(r.icon)}<span class="card__name">${esc(r.name)}</span></span>
        <span class="card__stat">${r.live ? "…" : "Not wired up yet"}</span>
      </a>`).join("");
  }

  for (const r of ROUTES) {
    if (!r.live) continue;
    const stat = stats[r.page] || { text: "—", tone: "" };
    const el = host.querySelector(`.card[data-page="${r.page}"] .card__stat`);
    if (!el) continue;
    if (el.innerHTML !== stat.text) el.innerHTML = stat.text;
    el.className = `card__stat${stat.tone ? ` card__stat--${stat.tone}` : ""}`;
  }
}

/** Applies `fn` only when the endpoint actually answered with an array. */
function statFor(value, fn) {
  return Array.isArray(value) ? fn(value) : { text: "Unreachable", tone: "warn" };
}

/* ── Theme toggle (shares the dashboard's `theme` key) ───────────────────── */

function currentTheme() {
  return document.documentElement.getAttribute("data-theme")
    || (window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark");
}

function paintToggle() {
  $("theme-toggle").innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true">${currentTheme() === "light" ? ICONS.moon : ICONS.sun}</svg>`;
}

$("theme-toggle").addEventListener("click", () => {
  const next = currentTheme() === "light" ? "dark" : "light";
  document.documentElement.setAttribute("data-theme", next);
  localStorage.setItem("theme", next);
  paintToggle();
});

/* ── Start ───────────────────────────────────────────────────────────────── */

paintToggle();
tickClock();
setInterval(tickClock, 1000);

poll();
setInterval(poll, POLL_MS);
