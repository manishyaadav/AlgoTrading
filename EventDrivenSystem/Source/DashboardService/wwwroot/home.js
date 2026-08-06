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

  // Components with no sourceable vendor mark, plus the metrics widgets.
  cloud: `<path d="M7 18a4.5 4.5 0 0 1-1-8.9A5.5 5.5 0 0 1 16.5 8a4 4 0 0 1 1.5 7.9"/><path d="M7 18h11"/>`,
  broadcast: `<circle cx="12" cy="12" r="1.8"/><path d="M8.5 8.5a5 5 0 0 0 0 7"/><path d="M15.5 8.5a5 5 0 0 1 0 7"/><path d="M5.5 5.5a9 9 0 0 0 0 13"/><path d="M18.5 5.5a9 9 0 0 1 0 13"/>`,
  bolt: `<path d="M13 3 5 14h6l-1 7 8-11h-6l1-7z"/>`,
  chip: `<rect x="7" y="7" width="10" height="10" rx="1.5"/><path d="M9 3v2.3M12 3v2.3M15 3v2.3M9 18.7V21M12 18.7V21M15 18.7V21M3 9h2.3M3 12h2.3M3 15h2.3M18.7 9H21M18.7 12H21M18.7 15H21"/>`,
  memory: `<rect x="3.5" y="7" width="17" height="10" rx="1.6"/><path d="M7 17v3M12 17v3M17 17v3M7 7V4M12 7V4M17 7V4"/>`,
  wallet: `<path d="M3.5 7.5A2 2 0 0 1 5.5 5.5h13a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z"/><path d="M16.5 11.5h4v3h-4a1.5 1.5 0 0 1 0-3z"/>`,
  shield: `<path d="M12 3.2 5 6v5.5c0 4 3 7.5 7 9.3 4-1.8 7-5.3 7-9.3V6z"/><path d="M9.3 12.2l1.9 1.9 3.6-3.9"/>`,
  receipt: `<path d="M6 3.5h12v17l-2.4-1.5-2.4 1.5-2.4-1.5-2.4 1.5L6 20.5z"/><path d="M9.2 8h5.6M9.2 12h5.6"/>`,
  layers: `<path d="M12 3.2 3.5 7.6 12 12l8.5-4.4z"/><path d="M3.5 12.4 12 16.8l8.5-4.4"/><path d="M3.5 16.6 12 21l8.5-4.4"/>`,
};

/* Vendor marks for the components that have one. Solid single paths from the
   Simple Icons set (CC0); drawn with fill:currentColor so they take the
   widget's state colour, and tint to the brand hue on hover.

   Azure Functions, Azurite and SignalR are absent on purpose: Microsoft had
   its marks withdrawn from the icon set, so there is no accurate source for
   them. Those widgets use the app's own glyphs rather than a guessed-at
   trademark — see ICONS above. */
const LOGOS = {
  kafka: { brand: "var(--brand-kafka)", box: "4.68 0 14.64 24", d: `M9.71 2.136a1.43 1.43 0 0 0-2.047 0h-.007a1.48 1.48 0 0 0-.421 1.042c0 .41.161.777.422 1.039l.007.007c.257.264.616.426 1.019.426.404 0 .766-.162 1.027-.426l.003-.007c.261-.262.421-.629.421-1.039 0-.408-.159-.777-.421-1.042H9.71zM8.683 22.295c.404 0 .766-.167 1.027-.429l.003-.008c.261-.261.421-.631.421-1.036 0-.41-.159-.778-.421-1.044H9.71a1.42 1.42 0 0 0-1.027-.432 1.4 1.4 0 0 0-1.02.432h-.007c-.26.266-.422.634-.422 1.044 0 .406.161.775.422 1.036l.007.008c.258.262.617.429 1.02.429zm7.89-4.462c.359-.096.683-.33.882-.684l.027-.052a1.47 1.47 0 0 0 .114-1.067 1.454 1.454 0 0 0-.675-.896l-.021-.014a1.425 1.425 0 0 0-1.078-.132c-.36.091-.684.335-.881.686-.2.349-.241.75-.146 1.119.099.363.33.691.675.896h.002c.346.203.737.239 1.101.144zm-6.405-7.342a2.083 2.083 0 0 0-1.485-.627c-.58 0-1.103.242-1.482.627-.378.385-.612.916-.612 1.507s.233 1.124.612 1.514a2.08 2.08 0 0 0 2.967 0c.379-.39.612-.923.612-1.514s-.233-1.122-.612-1.507zm-.835-2.51c.843.141 1.6.552 2.178 1.144h.004c.092.093.182.196.265.299l1.446-.851a3.176 3.176 0 0 1-.047-1.808 3.149 3.149 0 0 1 1.456-1.926l.025-.016a3.062 3.062 0 0 1 2.345-.306c.77.21 1.465.721 1.898 1.482v.002c.431.757.518 1.626.313 2.408a3.145 3.145 0 0 1-1.456 1.928l-.198.118h-.02a3.095 3.095 0 0 1-2.154.201 3.127 3.127 0 0 1-1.514-.944l-1.444.848a4.162 4.162 0 0 1 0 2.879l1.444.846c.413-.47.939-.789 1.514-.944a3.041 3.041 0 0 1 2.371.319l.048.023v.002a3.17 3.17 0 0 1 1.408 1.906 3.215 3.215 0 0 1-.313 2.405l-.026.053-.003-.005a3.147 3.147 0 0 1-1.867 1.436 3.096 3.096 0 0 1-2.371-.318v-.006a3.156 3.156 0 0 1-1.456-1.927 3.175 3.175 0 0 1 .047-1.805l-1.446-.848a3.905 3.905 0 0 1-.265.294l-.004.005a3.938 3.938 0 0 1-2.178 1.138v1.699a3.09 3.09 0 0 1 1.56.862l.002.004c.565.572.914 1.368.914 2.243 0 .873-.35 1.664-.914 2.239l-.002.009a3.1 3.1 0 0 1-2.21.931 3.1 3.1 0 0 1-2.206-.93h-.002v-.009a3.186 3.186 0 0 1-.916-2.239c0-.875.35-1.672.916-2.243v-.004h.002a3.1 3.1 0 0 1 1.558-.862v-1.699a3.926 3.926 0 0 1-2.176-1.138l-.006-.005a4.098 4.098 0 0 1-1.173-2.874c0-1.122.452-2.136 1.173-2.872h.006a3.947 3.947 0 0 1 2.176-1.144V6.289a3.137 3.137 0 0 1-1.558-.864h-.002v-.004a3.192 3.192 0 0 1-.916-2.243c0-.871.35-1.669.916-2.243l.002-.002A3.084 3.084 0 0 1 8.683 0c.861 0 1.641.355 2.21.932v.002h.002c.565.574.914 1.372.914 2.243 0 .876-.35 1.667-.914 2.243l-.002.005a3.142 3.142 0 0 1-1.56.864v1.692zm8.121-1.129l-.012-.019a1.452 1.452 0 0 0-.87-.668 1.43 1.43 0 0 0-1.103.146h.002c-.347.2-.58.529-.677.896-.095.365-.054.768.146 1.119l.007.009c.2.347.519.579.874.673.357.103.755.059 1.098-.144l.019-.009a1.47 1.47 0 0 0 .657-.885 1.493 1.493 0 0 0-.141-1.118` },
  redis: { brand: "var(--brand-redis)", box: "0 0.93 24 22.15", d: `M22.71 13.145c-1.66 2.092-3.452 4.483-7.038 4.483-3.203 0-4.397-2.825-4.48-5.12.701 1.484 2.073 2.685 4.214 2.63 4.117-.133 6.94-3.852 6.94-7.239 0-4.05-3.022-6.972-8.268-6.972-3.752 0-8.4 1.428-11.455 3.685C2.59 6.937 3.885 9.958 4.35 9.626c2.648-1.904 4.748-3.13 6.784-3.744C8.12 9.244.886 17.05 0 18.425c.1 1.261 1.66 4.648 2.424 4.648.232 0 .431-.133.664-.365a100.49 100.49 0 0 0 5.54-6.765c.222 3.104 1.748 6.898 6.014 6.898 3.819 0 7.604-2.756 9.33-8.965.2-.764-.73-1.361-1.261-.73zm-4.349-5.013c0 1.959-1.926 2.922-3.685 2.922-.941 0-1.664-.247-2.235-.568 1.051-1.592 2.092-3.225 3.21-4.973 1.972.334 2.71 1.43 2.71 2.619z` },
  rabbitmq: { brand: "var(--brand-rabbitmq)", box: "0 0 24 24", d: `M23.035 9.601h-7.677a.956.956 0 01-.962-.962V.962a.956.956 0 00-.962-.956H10.56a.956.956 0 00-.962.956V8.64a.956.956 0 01-.962.962H5.762a.956.956 0 01-.961-.962V.962A.956.956 0 003.839 0H.959a.956.956 0 00-.956.962v22.076A.956.956 0 00.965 24h22.07a.956.956 0 00.962-.962V10.58a.956.956 0 00-.962-.98zm-3.86 8.152a1.437 1.437 0 01-1.437 1.443h-1.924a1.437 1.437 0 01-1.436-1.443v-1.917a1.437 1.437 0 011.436-1.443h1.924a1.437 1.437 0 011.437 1.443z` },
  dotnet: { brand: "var(--brand-dotnet)", box: "0 7.53 24 8.94", d: `M24 8.77h-2.468v7.565h-1.425V8.77h-2.462V7.53H24zm-6.852 7.565h-4.821V7.53h4.63v1.24h-3.205v2.494h2.953v1.234h-2.953v2.604h3.396zm-6.708 0H8.882L4.78 9.863a2.896 2.896 0 0 1-.258-.51h-.036c.032.189.048.592.048 1.21v5.772H3.157V7.53h1.659l3.965 6.32c.167.261.275.442.323.54h.024c-.04-.233-.06-.629-.06-1.185V7.529h1.372zm-8.703-.693a.868.829 0 0 1-.869.829.868.829 0 0 1-.868-.83.868.829 0 0 1 .868-.828.868.829 0 0 1 .869.829Z` },
  docker: { brand: "var(--brand-docker)", box: "0 3.39 24 17.22", d: `M13.983 11.078h2.119a.186.186 0 00.186-.185V9.006a.186.186 0 00-.186-.186h-2.119a.185.185 0 00-.185.185v1.888c0 .102.083.185.185.185m-2.954-5.43h2.118a.186.186 0 00.186-.186V3.574a.186.186 0 00-.186-.185h-2.118a.185.185 0 00-.185.185v1.888c0 .102.082.185.185.185m0 2.716h2.118a.187.187 0 00.186-.186V6.29a.186.186 0 00-.186-.185h-2.118a.185.185 0 00-.185.185v1.887c0 .102.082.185.185.186m-2.93 0h2.12a.186.186 0 00.184-.186V6.29a.185.185 0 00-.185-.185H8.1a.185.185 0 00-.185.185v1.887c0 .102.083.185.185.186m-2.964 0h2.119a.186.186 0 00.185-.186V6.29a.185.185 0 00-.185-.185H5.136a.186.186 0 00-.186.185v1.887c0 .102.084.185.186.186m5.893 2.715h2.118a.186.186 0 00.186-.185V9.006a.186.186 0 00-.186-.186h-2.118a.185.185 0 00-.185.185v1.888c0 .102.082.185.185.185m-2.93 0h2.12a.185.185 0 00.184-.185V9.006a.185.185 0 00-.184-.186h-2.12a.185.185 0 00-.184.185v1.888c0 .102.083.185.185.185m-2.964 0h2.119a.185.185 0 00.185-.185V9.006a.185.185 0 00-.184-.186h-2.12a.186.186 0 00-.186.186v1.887c0 .102.084.185.186.185m-2.92 0h2.12a.185.185 0 00.184-.185V9.006a.185.185 0 00-.184-.186h-2.12a.185.185 0 00-.184.185v1.888c0 .102.082.185.185.185M23.763 9.89c-.065-.051-.672-.51-1.954-.51-.338.001-.676.03-1.01.087-.248-1.7-1.653-2.53-1.716-2.566l-.344-.199-.226.327c-.284.438-.49.922-.612 1.43-.23.97-.09 1.882.403 2.661-.595.332-1.55.413-1.744.42H.751a.751.751 0 00-.75.748 11.376 11.376 0 00.692 4.062c.545 1.428 1.355 2.48 2.41 3.124 1.18.723 3.1 1.137 5.275 1.137.983.003 1.963-.086 2.93-.266a12.248 12.248 0 003.823-1.389c.98-.567 1.86-1.288 2.61-2.136 1.252-1.418 1.998-2.997 2.553-4.4h.221c1.372 0 2.215-.549 2.68-1.009.309-.293.55-.65.707-1.046l.098-.288Z` },
};

// Destinations, in the same order as the dashboard's left nav. `live: false`
// marks the pages that are still placeholders — the card says so rather than
// pretending to have a number.
const ROUTES = [
  { page: "services", name: "Services & Connections", icon: "graph", live: true },
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
  renderWidgets(state, svc);
  renderRoutes(state, svc);
}

/* ── System widgets ─────────────────────────────────────────────────────────
   Every widget below reads something this stack actually reports. The ones
   the brief asked for that have no source yet — CPU/RAM (needs a Docker stats
   endpoint), RabbitMQ (not in this stack; it runs Kafka), and PnL / Risk /
   Orders / Positions (no execution engine exists) — are rendered in an
   explicit unwired state rather than filled with invented numbers. */

const WIDGET_HISTORY = new Map();   // id -> rolling values, for the sparklines

function pushHistory(id, value) {
  const arr = WIDGET_HISTORY.get(id) || [];
  arr.push(value);
  if (arr.length > 32) arr.shift();
  WIDGET_HISTORY.set(id, arr);
  return arr;
}

function containerState(services, match) {
  if (!Array.isArray(services)) return null;
  const found = services.filter(s => match.test(s.composeService || s.name || ""));
  if (!found.length) return null;
  return { total: found.length, up: found.filter(s => s.state === "running").length };
}

function widgetsFor(state, svc) {
  const s = state.services;
  const ex = primaryExchange(state.exchanges);
  const ingest = Array.isArray(state.ingestion) ? state.ingestion : [];
  const behind = behindCount(state.ingestion) + behindCount(state.aggregation);
  const bars = ingest.reduce((n, r) => n + (r.count || 0), 0);
  const stale = Array.isArray(state.freshness) ? state.freshness.filter(f => f.isStale).length : null;

  const infra = (label, re, mark) => {
    const c = containerState(s, re);
    if (!c) return { label, ...mark, value: null, sub: "not in this stack", tone: "" };
    return { label, ...mark, value: c.up, unit: `/${c.total}`, sub: c.up === c.total ? "healthy" : "degraded",
             tone: c.up === c.total ? "ok" : "bad", spark: true };
  };

  const engine = containerState(s, /strategy-live/i) || {};

  return [
    infra("Kafka", /kafka|zookeeper/i, { logo: "kafka" }),
    infra("Redis", /redis/i, { logo: "redis" }),
    infra("SignalR", /signalr/i, { icon: "broadcast" }),
    infra("Azure Functions", /country-live|exchange-live|holiday-live|ohlc-live|aggregation-live|notification-live|dataingestion|warmup/i, { icon: "bolt" }),
    infra("Azurite", /azurite/i, { icon: "cloud" }),
    { label: "RabbitMQ", logo: "rabbitmq", value: null, sub: "not in this stack — Kafka", tone: "" },

    { label: "Trading Engine", logo: "dotnet", value: engine.up ?? null,
      unit: "/1", sub: engine.up ? "running" : "down", tone: engine.up ? "ok" : "bad" },

    { label: "Exchange Connection", icon: "building", value: null,
      text: ex && ex.isToday ? stageLabel(ex.state) : "no session",
      sub: ex ? ex.exchangeName : "no exchange", tone: ex && ex.isToday ? "ok" : "warn" },

    { label: "Services", logo: "docker", value: svc.known ? svc.up : null, unit: svc.known ? `/${svc.total}` : "",
      sub: svc.known ? (svc.down ? `${svc.down} down` : "all running") : "docker unreachable",
      tone: svc.known ? (svc.down ? "bad" : "ok") : "bad", spark: true },

    { label: "Bars Ingested", icon: "stack", value: bars,
      sub: `${ingest.length} contracts · ${behind ? behind + " behind" : "on pace"}`,
      tone: behind ? "warn" : "ok", spark: true },

    { label: "Cache Freshness", icon: "freshness", value: stale, unit: stale === null ? "" : " stale",
      sub: Array.isArray(state.freshness) ? `${state.freshness.length} keys` : "unreachable",
      tone: stale === null ? "" : (stale ? "warn" : "ok"), spark: true },

    { label: "Strategies", icon: "target",
      value: Array.isArray(state.strategies) ? state.strategies.filter(x => x.deployedVersion).length : null,
      unit: Array.isArray(state.strategies) ? `/${state.strategies.length}` : "",
      sub: Array.isArray(state.strategies) ? "deployed" : "strategy-live unreachable",
      tone: Array.isArray(state.strategies) ? "ok" : "bad" },

    { label: "CPU Usage", icon: "chip",   value: null, sub: "needs /api/stats", tone: "" },
    { label: "RAM Usage", icon: "memory", value: null, sub: "needs /api/stats", tone: "" },
    { label: "PnL",       icon: "wallet", value: null, sub: "no execution engine", tone: "" },
    { label: "Risk",      icon: "shield", value: null, sub: "no execution engine", tone: "" },
    { label: "Orders",    icon: "receipt", value: null, sub: "no execution engine", tone: "" },
    { label: "Positions", icon: "layers", value: null, sub: "no execution engine", tone: "" },
  ];
}

/* Vendor marks are solid single paths; the app's own glyphs are stroked. Both
   ride on currentColor, so a widget's mark takes its state colour either way. */
function markHtml(w) {
  if (w.logo) {
    // viewBox is cropped to each mark's true bounds so they optically match at
    // a shared height — the marks range from 0.61 to 2.68 aspect, and a square
    // slot would squash the wordmarks to a fifth of the height of the rest.
    const l = LOGOS[w.logo];
    return `<svg class="widget__logo is-brand" viewBox="${l.box}" aria-hidden="true"><path d="${l.d}"/></svg>`;
  }
  if (w.icon) {
    return `<svg class="widget__logo" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[w.icon] || ""}</svg>`;
  }
  return "";
}

function renderWidgets(state, svc) {
  const host = $("widgets");
  const items = widgetsFor(state, svc);

  if (host.childElementCount !== items.length) {
    host.innerHTML = items.map((w, i) => `
      <div class="widget" data-w="${esc(w.label)}" style="--i:${i}${w.logo ? `; --brand:${LOGOS[w.logo].brand}` : ""}">
        <div class="widget__head">
          ${markHtml(w)}
          <span class="widget__label">${esc(w.label)}</span>
          <span class="widget__dot"></span>
        </div>
        <div class="widget__value">—</div>
        <div class="widget__sub"></div>
        ${w.spark ? '<svg class="widget__spark" aria-hidden="true"></svg>' : ""}
      </div>`).join("");
  }

  items.forEach((w) => {
    const el = host.querySelector(`.widget[data-w="${CSS.escape(w.label)}"]`);
    if (!el) return;

    el.classList.toggle("is-idle", w.value === null && !w.text);
    el.querySelector(".widget__dot").className = `widget__dot ${w.tone || ""}`;
    el.querySelector(".widget__sub").textContent = w.sub || "";

    const valEl = el.querySelector(".widget__value");
    if (w.text) {
      valEl.textContent = w.text;
      valEl.style.fontSize = "17px";
    } else if (w.value === null) {
      valEl.textContent = "—";
      valEl.style.fontSize = "";
    } else {
      valEl.style.fontSize = "";
      if (window.FX) FX.countUp(valEl, w.value, { suffix: w.unit || "" });
      else valEl.textContent = `${w.value}${w.unit || ""}`;
    }

    const sparkEl = el.querySelector(".widget__spark");
    if (sparkEl && w.value !== null && window.FX) {
      FX.spark(sparkEl, pushHistory(w.label, w.value));
    }
  });

  $("widgets-note").textContent =
    "Widgets without a value have no source in this stack yet — nothing here is filled with placeholder numbers.";
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
