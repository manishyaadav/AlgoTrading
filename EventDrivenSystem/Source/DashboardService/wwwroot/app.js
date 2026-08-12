const REFRESH_MS = 5000;
const FETCH_TIMEOUT_MS = 8000;

// Bounded with an explicit timeout — plain fetch() has none, so a single slow endpoint (found
// live: /api/aggregation took 15s+ once the day's Azurite CSV blobs grew large enough) could hang
// a card's load indefinitely. Fixed at the source too (the endpoints now run their per-ticker
// checks concurrently instead of one-at-a-time), but this stays as a second line of defense —
// same fix, same reasoning, as home.js's getJson().
async function fetchJson(url) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
  try {
    const res = await fetch(url, { signal: controller.signal });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.json();
  } finally {
    clearTimeout(timeout);
  }
}

// Strategy API base — must be declared before the initial showPage() call
// below (landing directly on #strategy runs loadStrategyGrid() synchronously
// during page load, which reads this). It used to be declared much further
// down, past that call, which threw "Cannot access before initialization"
// on every cold load of the Strategy page. Uses the current page's hostname
// rather than a hardcoded "localhost" so this also works when the dashboard
// is opened from another device on the LAN (e.g. a phone) via its IP.
const STRATEGY_API_BASE = `${location.protocol}//${location.hostname}:8096`;

// --- Live IST clock in the header (matches home.html's .stamp) ---
const IST_FMT = new Intl.DateTimeFormat("en-GB", {
  timeZone: "Asia/Kolkata",
  weekday: "short", day: "2-digit", month: "short", year: "numeric",
  hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
});

function tickClock() {
  const p = {};
  for (const { type, value } of IST_FMT.formatToParts(new Date())) p[type] = value;
  document.getElementById("stamp-date").textContent = `${p.weekday.toUpperCase()} ${p.day} ${p.month.toUpperCase()} ${p.year}`;
  document.getElementById("stamp-time").textContent = `${p.hour}:${p.minute}:${p.second}`;
}
tickClock();
setInterval(tickClock, 1000);

// Small self-contained icon set (24x24 stroke icons) — no external requests, no icon font/library.
const ICONS = {
  stream: `<path d="M4 6h16M4 12h16M4 18h10"/>`,                                   // Kafka / Zookeeper / Kafdrop
  database: `<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>`, // Redis
  cloud: `<path d="M7 18a4.5 4.5 0 0 1-1-8.9A5.5 5.5 0 0 1 16.5 8a4 4 0 0 1 1.5 7.9"/><path d="M7 18h11"/>`, // Azurite
  broadcast: `<circle cx="12" cy="12" r="1.8"/><path d="M8.5 8.5a5 5 0 0 0 0 7"/><path d="M15.5 8.5a5 5 0 0 1 0 7"/><path d="M5.5 5.5a9 9 0 0 0 0 13"/><path d="M18.5 5.5a9 9 0 0 1 0 13"/>`, // SignalR
  gauge: `<path d="M12 12l4-3"/><circle cx="12" cy="12" r="9"/><path d="M7 15a6 6 0 0 1 10 0"/>`, // Dashboard
  bolt: `<path d="M13 3 5 14h6l-1 7 8-11h-6l1-7z"/>`,  // Function apps (dataingestion/holiday/ohlc/country/exchange/aggregation/notification)
  graph: `<circle cx="6" cy="6" r="2.4"/><circle cx="18" cy="6" r="2.4"/><circle cx="12" cy="18" r="2.4"/><path d="M8 7l7-1M8.5 8l3 8M15.5 8l-3 8"/>`,
  sun: `<circle cx="12" cy="12" r="4.5"/><path d="M12 2.5v3M12 18.5v3M4.5 12h-3M22.5 12h-3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1"/>`,
  moon: `<path d="M20 14.5A8.5 8.5 0 1 1 9.5 4a7 7 0 0 0 10.5 10.5z"/>`,
  columns: `<rect x="3" y="4" width="6" height="16" rx="1"/><rect x="9.5" y="4" width="6" height="16" rx="1"/><rect x="16" y="4" width="5" height="16" rx="1"/>`,
  rows: `<rect x="3" y="3" width="18" height="5.3" rx="1"/><rect x="3" y="9.3" width="18" height="5.3" rx="1"/><rect x="3" y="15.7" width="18" height="5.3" rx="1"/>`,
  // colorful category icons
  stack: `<rect x="4" y="3.5" width="16" height="5" rx="1.3"/><rect x="4" y="10" width="16" height="5" rx="1.3"/><rect x="4" y="16.5" width="16" height="4.5" rx="1.3"/><circle cx="7.3" cy="6" r="0.7" fill="currentColor" stroke="none"/><circle cx="7.3" cy="12.5" r="0.7" fill="currentColor" stroke="none"/><circle cx="7.3" cy="18.7" r="0.7" fill="currentColor" stroke="none"/>`,
  wrench: `<path d="M21 7.2a5.3 5.3 0 0 1-7.3 4.9L6 19.8l-2-2 7.7-7.7A5.3 5.3 0 1 1 21 7.2z"/>`,
  chip: `<rect x="7" y="7" width="10" height="10" rx="1.5"/><path d="M9 3v2.3M12 3v2.3M15 3v2.3M9 18.7V21M12 18.7V21M15 18.7V21M3 9h2.3M3 12h2.3M3 15h2.3M18.7 9H21M18.7 12H21M18.7 15H21"/>`,
  // left-nav icons
  target: `<circle cx="12" cy="12" r="8.5"/><circle cx="12" cy="12" r="4.8"/><circle cx="12" cy="12" r="1" fill="currentColor" stroke="none"/>`,
  sync: `<path d="M4.5 12a7.5 7.5 0 0 1 13-5.2M17.5 3.5v4.3h-4.3"/><path d="M19.5 12a7.5 7.5 0 0 1-13 5.2M6.5 20.5v-4.3h4.3"/>`,
  history: `<path d="M3.5 12a8.5 8.5 0 1 0 2.8-6.3"/><path d="M3.5 3.8v4.3h4.3"/><path d="M12 8v4.5l3 2"/>`,
  sliders: `<path d="M4 6h6M14 6h6M4 12h10M18 12h2M4 18h13M21 18h0"/><circle cx="12" cy="6" r="2"/><circle cx="16" cy="12" r="2"/><circle cx="19" cy="18" r="2"/>`,
  bell: `<path d="M6.5 8.5a5.5 5.5 0 0 1 11 0c0 4.5 1.8 5.8 1.8 5.8H4.7s1.8-1.3 1.8-5.8z"/><path d="M9.7 18a2.3 2.3 0 0 0 4.6 0"/>`,
  building: `<path d="M4 21h16M6 21V6.5L12 3l6 3.5V21"/><path d="M9.5 21v-5h5v5"/><path d="M9 9h1.4M13.6 9H15M9 13h1.4M13.6 13H15"/>`,
  pulse: `<path d="M4 12h4l2-7 4 14 2-7h4"/>`, // Rule Engine — a live signal being checked, not a static gear
};

function iconFor(composeService) {
  const n = (composeService || "").toLowerCase();
  if (n.includes("kafka") || n.includes("zookeeper") || n.includes("kafdrop")) return "stream";
  if (n.includes("redis")) return "database";
  if (n.includes("azurite")) return "cloud";
  if (n.includes("signalr")) return "broadcast";
  if (n.includes("dashboard")) return "gauge";
  return "bolt";
}

// Which of the 3 sections each compose service belongs to. Docker has no notion of this
// grouping (unlike depends_on), so it's a hand-maintained map — update it when a new
// service is added to docker-compose-live.yml.
const CATEGORIES = [
  {
    key: "infra",
    title: "Infrastructure",
    icon: "stack",
    services: ["redis-live", "kafka-live", "zookeeper-live", "kafdrop-live", "azurite-live", "dashboard-live"],
  },
  {
    key: "helpers",
    title: "Helpers",
    icon: "wrench",
    services: ["holiday-live", "ohlc-live", "signalr-live"],
  },
  {
    key: "core",
    title: "Core Services",
    icon: "chip",
    services: ["dataingestion", "country-live", "exchange-live", "aggregation-live", "notification-live"],
  },
];

function categoryFor(composeService) {
  return CATEGORIES.find(c => c.services.includes(composeService)) || CATEGORIES[CATEGORIES.length - 1];
}

function svgIcon(name, extraClass) {
  const path = ICONS[name] || ICONS.bolt;
  return `<svg class="icon ${extraClass || ""}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">${path}</svg>`;
}

document.querySelectorAll(".section-icon, .placeholder-icon").forEach(el => {
  el.innerHTML = svgIcon(el.dataset.icon);
});

// --- left nav / page switching (URL hash-based, so pages are linkable and back/forward work) ---
const PAGES = document.querySelectorAll(".page");
const NAV_ITEMS = document.querySelectorAll(".nav-item");
const DEFAULT_PAGE = "services";

function showPage(key) {
  const target = Array.from(PAGES).some(p => p.dataset.page === key) ? key : DEFAULT_PAGE;
  PAGES.forEach(p => p.classList.toggle("active", p.dataset.page === target));
  NAV_ITEMS.forEach(n => n.classList.toggle("active", n.dataset.page === target));

  // the connection-arrow math needs real (non-zero) layout size, which a hidden `.page`
  // doesn't have — redraw once it's actually visible instead of relying on a stale measurement
  if (target === "services") {
    requestAnimationFrame(() => drawConnections(lastServices));
  }

  // refresh on every visit rather than once at page load, so edits/deploys from a previous
  // visit (or from another browser tab) aren't shown stale; also drop any open view/edit panel
  if (target === "strategy") {
    closeStrategyPanel();
    loadStrategyGrid();
  }

  if (target === "exchanges") {
    loadCountryStatus();
    loadExchangeTimelines();
  }

  if (target === "data") {
    loadIngestionStatus();
    loadAggregationStatus();
    loadIndicatorsStatus();
  }

  if (target === "rule-engine") {
    loadRuleEngine();
  }

  // Just populates the strategy picker — running the backtest itself is an explicit, expensive
  // user action (POST .../backtest), never something the 5s refresh() cycle should trigger.
  if (target === "backtest") {
    loadBacktestForm();
  }
}

NAV_ITEMS.forEach(btn => {
  btn.querySelector(".nav-icon").innerHTML = svgIcon(btn.dataset.icon);
  btn.addEventListener("click", () => { location.hash = btn.dataset.page; });
});

window.addEventListener("hashchange", () => showPage(location.hash.replace("#", "")));
showPage(location.hash.replace("#", "") || DEFAULT_PAGE);

function currentTheme() {
  return document.documentElement.getAttribute("data-theme")
    || (window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark");
}

function renderThemeToggle() {
  const btn = document.getElementById("theme-toggle");
  // icon shown = the theme you'd switch TO
  btn.innerHTML = svgIcon(currentTheme() === "dark" ? "sun" : "moon");
}

document.getElementById("theme-toggle").addEventListener("click", () => {
  const next = currentTheme() === "dark" ? "light" : "dark";
  document.documentElement.setAttribute("data-theme", next);
  localStorage.setItem("theme", next);
  renderThemeToggle();
  requestAnimationFrame(() => drawConnections(lastServices)); // line color depends on theme
});

renderThemeToggle();

function currentOrientation() {
  return localStorage.getItem("orientation") || "horizontal";
}

function renderOrientationToggle() {
  const btn = document.getElementById("orientation-toggle");
  const orientation = currentOrientation();
  document.getElementById("services").setAttribute("data-orientation", orientation);
  // icon shown = the layout you'd switch TO
  btn.innerHTML = svgIcon(orientation === "horizontal" ? "rows" : "columns");
}

document.getElementById("orientation-toggle").addEventListener("click", () => {
  const next = currentOrientation() === "horizontal" ? "vertical" : "horizontal";
  localStorage.setItem("orientation", next);
  renderOrientationToggle();
  requestAnimationFrame(() => drawConnections(lastServices)); // panel positions changed
});

renderOrientationToggle();

function stateClass(state) {
  if (state === "running") return "running";
  if (state === "exited" || state === "dead") return "exited";
  return "other";
}

let lastServices = [];

// How many other containers declare a depends_on pointing at this one.
function dependentsOf(composeService, services) {
  return services.filter(x => (x.dependsOn || []).includes(composeService)).length;
}

function linksLine(s, services) {
  const deps = (s.dependsOn || []).length;
  const dependents = dependentsOf(s.composeService, services);
  if (!deps && !dependents) return "";
  const parts = [];
  if (deps) parts.push(`↓ ${deps} dep${deps === 1 ? "" : "s"}`);
  if (dependents) parts.push(`↑ ${dependents} dependent${dependents === 1 ? "" : "s"}`);
  return parts.join(" · ");
}

function serviceCard(s, services) {
  const links = linksLine(s, services);
  return `
    <div class="card" id="card-${cssId(s.composeService)}" data-service="${s.composeService}" tabindex="0">
      <div class="name">${svgIcon(iconFor(s.composeService))}<span>${s.composeService || s.name}</span></div>
      <div class="status-line">
        <span class="dot ${stateClass(s.state)}"></span>
        <span class="card-status">${s.status}</span>
      </div>
      <div class="status-line card-ports">${s.ports.length ? s.ports.join(", ") : ""}</div>
      <div class="links">${links}</div>
    </div>
  `;
}

// Refreshing every 5s must not throw the DOM away: rebuilding would cancel any
// hover, drop keyboard focus, and kill the dependency highlight mid-inspection.
// The panels are rebuilt only when the set of containers actually changes.
function servicesSignature(services) {
  return services.map(s => `${categoryFor(s.composeService).key}/${s.composeService}`).sort().join("|");
}

function updateServiceCard(s, services) {
  const card = document.getElementById(`card-${cssId(s.composeService)}`);
  if (!card) return;

  const dot = card.querySelector(".dot");
  dot.className = `dot ${stateClass(s.state)}`;
  card.querySelector(".card-status").textContent = s.status;
  card.querySelector(".card-ports").textContent = s.ports.length ? s.ports.join(", ") : "";
  card.querySelector(".links").textContent = linksLine(s, services);
}

async function loadServices() {
  const el = document.getElementById("services");
  try {
    const res = await fetch("/api/services");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const services = await res.json();
    lastServices = services;

    if (!services.length) {
      el.dataset.signature = "";
      el.innerHTML = `<div class="error">No containers found for this compose project.</div>`;
      drawConnections([]);
      return;
    }

    const signature = servicesSignature(services);
    const rebuilt = el.dataset.signature !== signature;

    if (rebuilt) {
      el.dataset.signature = signature;
      el.innerHTML = CATEGORIES.map(cat => {
        const items = services.filter(s => categoryFor(s.composeService).key === cat.key);
        return `
          <div class="category-panel" data-category="${cat.key}">
            <div class="category-header">
              ${svgIcon(cat.icon, "category-icon")}
              <span>${cat.title}</span>
              <span class="category-count"></span>
            </div>
            <div class="cards-mini">
              ${items.length ? items.map(s => serviceCard(s, services)).join("") : `<div class="empty">No services</div>`}
            </div>
          </div>
        `;
      }).join("");
    }

    services.forEach(s => updateServiceCard(s, services));

    // The panel's top rule and count report the group's health, so a container
    // dropping out is visible without opening the group.
    CATEGORIES.forEach(cat => {
      const panel = el.querySelector(`.category-panel[data-category="${cat.key}"]`);
      if (!panel) return;
      const items = services.filter(s => categoryFor(s.composeService).key === cat.key);
      const up = items.filter(s => s.state === "running").length;
      panel.dataset.health = items.length && up === items.length ? "ok" : "down";
      panel.querySelector(".category-count").innerHTML = `<b>${up}</b>/${items.length} up`;
    });

    // let the DOM settle before measuring positions
    if (rebuilt) requestAnimationFrame(() => drawConnections(services));
  } catch (err) {
    el.dataset.signature = "";
    el.innerHTML = `<div class="error">Unable to load service status: ${err.message}</div>`;
    drawConnections([]);
  }
}

/* --- Dependency focus -----------------------------------------------------
   Hovering or keyboard-focusing a service lights the depends_on edges touching
   it and dims the rest, so the subgraph around one container is legible without
   a legend. Everything here reads the same live Docker data the arrows do. */

function relatedTo(composeService) {
  const self = lastServices.find(s => s.composeService === composeService);
  const related = new Set(self ? (self.dependsOn || []) : []);
  lastServices.forEach(s => {
    if ((s.dependsOn || []).includes(composeService)) related.add(s.composeService);
  });
  return related;
}

function focusService(composeService) {
  const row = document.getElementById("services");
  const related = relatedTo(composeService);

  row.classList.add("is-focusing");
  document.getElementById("services-graph").classList.add("is-focusing");

  row.querySelectorAll(".card").forEach(card => {
    const name = card.dataset.service;
    card.classList.toggle("is-focused", name === composeService);
    card.classList.toggle("is-related", related.has(name));
  });

  document.querySelectorAll("#connections .connection").forEach(line => {
    line.classList.toggle("is-lit", line.dataset.from === composeService || line.dataset.to === composeService);
  });
}

function clearServiceFocus() {
  document.getElementById("services").classList.remove("is-focusing");
  document.getElementById("services-graph").classList.remove("is-focusing");
  document.querySelectorAll("#services .card").forEach(c => c.classList.remove("is-focused", "is-related"));
  document.querySelectorAll("#connections .connection").forEach(l => l.classList.remove("is-lit"));
}

// Delegated, so the handlers survive a rebuild of the panels.
(function wireServiceFocus() {
  const row = document.getElementById("services");

  row.addEventListener("mouseover", e => {
    const card = e.target.closest(".card[data-service]");
    if (card) focusService(card.dataset.service);
    else if (!row.contains(document.activeElement)) clearServiceFocus();
  });
  row.addEventListener("mouseleave", () => {
    if (!row.contains(document.activeElement)) clearServiceFocus();
  });

  row.addEventListener("focusin", e => {
    const card = e.target.closest(".card[data-service]");
    if (card) focusService(card.dataset.service);
  });
  row.addEventListener("focusout", e => {
    if (!row.contains(e.relatedTarget)) clearServiceFocus();
  });
})();

function cssId(name) {
  return (name || "unknown").replace(/[^a-zA-Z0-9_-]/g, "_");
}

function drawConnections(services) {
  const wrapper = document.getElementById("services-graph");
  const svg = document.getElementById("connections");
  svg.innerHTML = "";

  if (!services.length) return;

  const wrapperRect = wrapper.getBoundingClientRect();
  svg.setAttribute("width", wrapperRect.width);
  svg.setAttribute("height", wrapperRect.height);

  svg.innerHTML = `
    <defs>
      <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
        <path d="M0 0L10 5L0 10z" fill="currentColor"></path>
      </marker>
    </defs>
  `;

  const centerOf = (composeService) => {
    const card = document.getElementById(`card-${cssId(composeService)}`);
    if (!card) return null;
    const r = card.getBoundingClientRect();
    return {
      x: r.left - wrapperRect.left + r.width / 2,
      y: r.top - wrapperRect.top + r.height / 2,
      halfW: r.width / 2,
      halfH: r.height / 2,
    };
  };

  services.forEach(s => {
    (s.dependsOn || []).forEach(depName => {
      const from = centerOf(s.composeService);
      const to = centerOf(depName);
      if (!from || !to) return;

      // trim the line so it stops at the card edge instead of overlapping the card
      const dx = to.x - from.x, dy = to.y - from.y;
      const dist = Math.hypot(dx, dy) || 1;
      const ux = dx / dist, uy = dy / dist;
      const x1 = from.x + ux * (from.halfW * 0.9);
      const y1 = from.y + uy * (from.halfH * 0.9);
      const x2 = to.x - ux * (to.halfW * 1.1);
      const y2 = to.y - uy * (to.halfH * 1.1);

      const line = document.createElementNS("http://www.w3.org/2000/svg", "line");
      line.setAttribute("x1", x1);
      line.setAttribute("y1", y1);
      line.setAttribute("x2", x2);
      line.setAttribute("y2", y2);
      line.setAttribute("class", "connection");
      line.setAttribute("marker-end", "url(#arrow)");
      // lets the dependency-focus highlight find the edges touching a service
      line.dataset.from = s.composeService;
      line.dataset.to = depName;
      svg.appendChild(line);
    });
  });
}

// Redis writes these as IST-local strings with no zone suffix, so `new Date()`
// would re-read them in the viewer's timezone and shift every row. Slice the
// parts out instead — same reasoning as clockTime() on the Data page.
// --- Exchanges page: country gate + exchange session timeline ---

const EXCHANGE_STAGES = [
  { key: "Initiated", label: "Init", time: "09:00" },
  { key: "PreOpened", label: "Pre-Open", time: "09:07" },
  { key: "Opened", label: "Open", time: "09:15" },
  { key: "PreClosed", label: "Pre-Close", time: "15:15" },
  { key: "Closed", label: "Close", time: "15:30" },
];

function stageLabelFor(key) {
  const stage = EXCHANGE_STAGES.find(s => s.key === key);
  return stage ? stage.label : (key || "unknown");
}

function countryStatusHtml(c) {
  if (!c.found) {
    return `<div class="hint">No country data in Redis yet — country-live hasn't run since this stack started.</div>`;
  }

  const stateClass = c.state === "Normal" ? "badge-deployed" : c.state === "Holiday" || c.state === "Weekend" ? "badge-not-deployed" : "";
  const staleBadge = c.isToday ? "" : `<span class="badge badge-not-deployed">Stale — last run ${esc(c.date)}, not today</span>`;

  return `
    <div class="status-card">
      <div class="status-card-name">${esc(c.name)}</div>
      <div class="status-card-badges">
        <span class="badge ${stateClass}">${esc(c.state)}</span>
        ${staleBadge}
      </div>
      <div class="status-card-meta">
        <div><b>Date</b>${esc(c.date)}</div>
        ${c.holiday ? `<div><b>Holiday today</b>${esc(c.holiday.reason)} (${esc(c.holiday.date)})</div>` : ""}
        ${c.nextHoliday ? `<div><b>Next holiday</b>${esc(c.nextHoliday.reason)} (${esc(c.nextHoliday.date)})</div>` : ""}
        <div><b>Updated</b>${esc(clockTime(c.updatedOn))}</div>
      </div>
    </div>
  `;
}

async function loadCountryStatus() {
  const el = document.getElementById("country-status");
  try {
    const res = await fetch("/api/country");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    lastCountry = await res.json();
    el.innerHTML = countryStatusHtml(lastCountry);
  } catch (err) {
    lastCountry = null;
    el.innerHTML = `<div class="error">Unable to load country status: ${err.message}</div>`;
  }
}

function exchangeTimelineHtml(ex) {
  // Stages fire strictly in order through the day and each overwrites the same Redis key, so
  // "current state's position in the sequence" reliably implies every earlier stage already
  // fired too — no need to track each of the 5 stages independently.
  const currentIdx = ex.isToday ? EXCHANGE_STAGES.findIndex(s => s.key === ex.state) : -1;

  // Three states, matching the console's gate row: the stage we're at now reads
  // differently from the ones already behind us, which the old two-state
  // done/pending split couldn't show.
  const stageClass = (i) => i < currentIdx ? "done" : i === currentIdx ? "done current" : "pending";

  const stages = EXCHANGE_STAGES.map((s, i) => `
    <div class="timeline-stage ${stageClass(i)}">
      <div class="timeline-dot"></div>
      <div class="timeline-label">${s.label}<div class="timeline-time">${s.time}</div></div>
    </div>
    ${i < EXCHANGE_STAGES.length - 1 ? `<div class="timeline-connector ${i < currentIdx ? "done" : "pending"}"></div>` : ""}
  `).join("");

  const metaText = ex.isToday
    ? `Now at ${esc(stageLabelFor(ex.state))} · updated ${esc(clockTime(ex.updatedOn))}`
    : `No data for today yet — last known: ${esc(ex.date || "—")}`;

  return `
    <div class="exchange-row">
      <div class="exchange-row-name">${esc(ex.exchangeName)}</div>
      <div class="timeline">${stages}</div>
      <div class="exchange-row-meta">${metaText}</div>
    </div>
  `;
}

async function loadExchangeTimelines() {
  const el = document.getElementById("exchange-timelines");
  try {
    const res = await fetch("/api/exchanges");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const exchanges = await res.json();
    lastExchanges = exchanges;

    el.innerHTML = exchanges.length
      ? exchanges.map(exchangeTimelineHtml).join("")
      : `<div class="hint">No exchange data in Redis yet — exchange-live hasn't run since this stack started.</div>`;
  } catch (err) {
    lastExchanges = [];
    el.innerHTML = `<div class="error">Unable to load exchange timelines: ${err.message}</div>`;
  }
}

/* --- Session phase -------------------------------------------------------
   Same computation home.js runs, over the same payloads — refresh() already
   polls country and exchanges on every cycle regardless of which page is
   showing, so this costs no extra requests. It drives --phase, which colours
   the page wash and the mark; interactive colour stays on --accent. */

let lastCountry = null;
let lastExchanges = [];

const SESSION_OPEN_MIN = 9 * 60 + 15;
const SESSION_CLOSE_MIN = 15 * 60 + 30;

// Explicit IST via the header clock's formatter, matching the backend's
// IstNow() — correct from any viewer timezone.
function istMinuteOfDay() {
  const p = {};
  for (const { type, value } of IST_FMT.formatToParts(new Date())) p[type] = value;
  return Number(p.hour) * 60 + Number(p.minute);
}

function computePhase() {
  if (lastServices.length && lastServices.some(s => s.state !== "running")) return "down";

  const c = lastCountry;
  if (!c || !c.found || !c.isToday) return "off";
  if (c.state && c.state !== "Normal") return "off";

  const ex = lastExchanges.find(e => /nse/i.test(e.exchangeName)) || lastExchanges[0];
  if (ex && ex.isToday && ex.state === "PreClosed") return "late";

  const m = istMinuteOfDay();
  if (m < SESSION_OPEN_MIN) return "pre";
  if (m >= SESSION_CLOSE_MIN) return "closed";
  return "open";
}

function applyPhase() {
  const phase = computePhase();
  document.documentElement.setAttribute("data-phase", phase);
  renderHeaderStatus(phase);
}

// Header status pill (visible on every page, between the mark and the clock).
// Same lastCountry/lastExchanges computePhase() already has — no extra request.
// Phrased like the console's verdict ("Normal Day · Session Over (NSE, NFO)"):
// day state first (Normal/Holiday/Weekend/not gated), then what the session is
// doing right now, then which exchanges that covers — never a fixed list, just
// whatever exchange-live has actually reported. "closed" already covers the
// full 15:30→midnight window (istMinuteOfDay runs 0-1439; nothing resets it
// early) — it only stops applying once the calendar date rolls over and
// country-live re-gates the new day, same as every other "isToday" check here.
function renderHeaderStatus(phase) {
  const textEl = document.getElementById("header-status-text");
  const pillEl = document.getElementById("header-status");
  if (!textEl || !pillEl) return;

  const c = lastCountry;
  const exchangeNames = lastExchanges.map(e => e.exchangeName).join(", ");
  const suffix = exchangeNames ? ` (${exchangeNames})` : "";

  let text, title;

  if (phase === "down") {
    text = "Service Issue";
    title = "One or more containers are not running — see Services & Connections";
  } else if (!c || !c.found) {
    text = "Not Gated Yet";
    title = "country-live hasn't run since this stack started";
  } else if (!c.isToday) {
    text = `${c.state} Day · Stale`;
    title = `Country state last set ${c.date}, not today`;
  } else if (c.state === "Holiday") {
    text = `Holiday${suffix}`;
    title = c.holiday ? `${c.holiday.reason} (${c.holiday.date})` : "Holiday — no session today";
  } else if (c.state === "Weekend") {
    text = `Weekend${suffix}`;
    title = "No session — weekend";
  } else if (phase === "pre") {
    text = `Normal Day Session Starts Soon${suffix}`;
    title = "Opens at 09:15 IST";
  } else if (phase === "late") {
    text = `Normal Day Pre-Close${suffix}`;
    title = "Closes at 15:30 IST";
  } else if (phase === "closed") {
    text = `Normal Day Session Over${suffix}`;
    title = "Session ended at 15:30 IST";
  } else if (exchangeNames) {
    text = `Normal Day Market Open${suffix}`;
    title = "Session runs 09:15–15:30 IST";
  } else {
    text = "Normal Day · No Exchange Data";
    title = "No exchange has reported yet";
  }

  textEl.textContent = text;
  pillEl.title = title;
}

// --- Data page: ingestion + aggregation candle-count status, per contract ---

const STATUS_LABELS = { green: "On Track", amber: "Behind", red: "Behind / No Data", pending: "Pending" };

// Redis stores these as IST-local strings with no zone suffix, so slicing the
// time out is correct where `new Date(...)` would silently re-interpret them in
// the viewer's timezone. Everything on this page is one trading day anyway, so
// the date carries no information.
function clockTime(iso) {
  if (!iso) return "—";
  const m = String(iso).match(/T(\d{2}:\d{2}:\d{2})/);
  return m ? m[1] : String(iso);
}

// Non-hardcoded color per instrument — same ticker always gets the same
// --tag-N across both Ingestion and Aggregation cards, without ever mapping a
// specific ticker name to a specific color (this app discovers tickers
// dynamically; a hand-maintained map would silently miss new ones, the same
// trap the old Services page CATEGORIES color scheme fell into).
//
// Assigned in first-discovery order (not hashed) and cached for the page's
// lifetime: a hash has no collision guarantee — BANKNIFTY and NIFTY landed on
// the identical tag under a straightforward string hash — while first-seen
// ordinal assignment guarantees every distinct ticker gets its own color as
// long as there are no more than INSTRUMENT_TAGS of them on screen at once,
// which covers every contract list this dashboard has ever shown.
const INSTRUMENT_TAGS = 5;
const instrumentTagByName = new Map();
function instrumentColorVar(name) {
  const key = String(name || "");
  if (!instrumentTagByName.has(key)) {
    instrumentTagByName.set(key, (instrumentTagByName.size % INSTRUMENT_TAGS) + 1);
  }
  return `var(--tag-${instrumentTagByName.get(key)})`;
}

// Ingested is always green — never tied to session mood/status. Missing (expected by now, isn't
// there — a genuine, permanent gap) is always red. Not-yet-due is the dim "rest of session" tone.
// Three fixed meanings, not a palette that drifts with anything else.
const BUCKET_COLORS = { a: "var(--green)", m: "var(--red)", p: "var(--border)" };

// Turns the backend's per-bucket map ("aaaammmpppp...", one char per expected bucket) into a
// linear-gradient with one color-stop pair per state *transition*, not per bucket — a 375-bucket
// 1-min row costs a handful of stops, not 375. The tick rhythm is a separate mask in CSS
// (.rail-map), so this never needs to know or care about it.
function bucketMapGradient(map, colors) {
  const total = map.length || 1;
  const stops = [];
  let i = 0;
  while (i < total) {
    const c = map[i];
    let j = i;
    while (j < total && map[j] === c) j++;
    const color = colors[c] || colors.p;
    stops.push(`${color} ${(i / total * 100).toFixed(3)}%`, `${color} ${(j / total * 100).toFixed(3)}%`);
    i = j;
  }
  return `linear-gradient(90deg, ${stops.join(",")})`;
}

function candleStatusCardHtml(item) {
  const total = item.expectedTotal || 1;
  const exp = Math.min(100, (item.expectedSoFar / total) * 100);
  const label = STATUS_LABELS[item.status] || item.status;
  const short = item.expectedSoFar - item.count;
  const map = item.bucketMap || "";

  // One tick per expected bar, on the same 09:15→15:30 axis the console uses.
  const rail = `
    <div class="rail"
         role="img" aria-label="${esc(item.contract)} ${item.timeframe} minute: ${item.count} of ${item.expectedTotal} bars, ${item.expectedSoFar} expected by now">
      <div class="rail-map" style="--bars:${total}; background-image:${bucketMapGradient(map, BUCKET_COLORS)}"></div>
      <div class="rail-now" style="--exp:${exp}%"></div>
    </div>`;

  const meta = [
    item.provider ? esc(item.provider) : null,
    `latest ${esc(clockTime(item.latestWindowStartTime))}`,
    `updated ${esc(clockTime(item.updatedOn))}`,
  ].filter(Boolean).join(" · ");

  return `
    <div class="candle-row">
      <div class="candle-row-head">
        <div>
          <span class="candle-row-name" style="color:${instrumentColorVar(item.contract)}">${esc(item.contract)}</span>
          <span class="candle-tf">${item.timeframe}m</span>
        </div>
        <div>
          <span class="badge status-${item.status}">${esc(label)}</span>
          <span class="candle-count"><b>${item.count}</b> / ${item.expectedTotal} bars</span>
        </div>
      </div>
      ${rail}
      <div class="rail-axis">
        <span>09:15</span>
        <span>${short > 0 ? `<b>${short}</b> behind · ` : ""}<b>${item.expectedSoFar}</b> expected by now</span>
        <span>15:30</span>
      </div>
      <div class="candle-row-foot">
        <span class="candle-row-meta">${meta}</span>
        <span class="storage-indicators">
          <span class="storage-pill ${item.inRedis ? "ok" : "bad"}"><span class="storage-dot"></span>Redis</span>
          <span class="storage-pill ${item.inAzurite ? "ok" : "bad"}"><span class="storage-dot"></span>Azurite</span>
        </span>
      </div>
    </div>
  `;
}

async function loadCandleStatus(endpoint, elementId, emptyText) {
  const el = document.getElementById(elementId);
  try {
    const items = await fetchJson(endpoint);

    el.innerHTML = items.length
      ? items.map(candleStatusCardHtml).join("")
      : `<div class="hint">${emptyText}</div>`;
  } catch (err) {
    el.innerHTML = `<div class="error">Unable to load: ${err.message}</div>`;
  }
}

function loadIngestionStatus() {
  return loadCandleStatus("/api/data-ingestion", "ingestion-status", "No ingestion data yet — nothing has flowed through dataingestion today.");
}
function loadAggregationStatus() {
  return loadCandleStatus("/api/aggregation", "aggregation-status", "No aggregation data yet — nothing has flowed through aggregation-live today.");
}

// --- Data page: Indicators (EMA / Supertrend / Pivot Central Range) ---

// Deliberately separate from STATUS_LABELS above — "On Track"/"Behind" reads fine for a bar-count
// progress card, not for "is this indicator's value current". Same four colors, different words.
const INDICATOR_STATUS_LABELS = { green: "Live", amber: "Delayed", red: "Stale", pending: "Seeding…" };

function formatIndicatorValue(value) {
  if (value === null || value === undefined || value === "") return "—";
  const n = Number(value);
  if (Number.isNaN(n)) return esc(value);
  // Indicator values run to many decimal places internally (EMA's recursive formula never
  // rounds) — display precision, not storage precision.
  return n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function indicatorCardHtml(item) {
  const label = INDICATOR_STATUS_LABELS[item.status] || item.status;
  const refLabel = item.period > 0
    ? `${esc(item.reference)}(${item.period}${item.multiplier ? "," + item.multiplier : ""})`
    : esc(item.reference);

  const seedBar = !item.isSeeded && item.seedProgress
    ? (() => {
        const [seen, need] = item.seedProgress.split("/").map(Number);
        const pct = need > 0 ? Math.min(100, (seen / need) * 100) : 0;
        return `
          <div class="indicator-seed">
            <div class="indicator-seed-track"><div class="indicator-seed-fill" style="width:${pct}%"></div></div>
            <span>${esc(item.seedProgress)} bars seeded</span>
          </div>`;
      })()
    : "";

  const directionBadge = item.direction
    ? `<span class="badge status-${item.direction === "Up" ? "green" : "red"}">${esc(item.direction)}</span>`
    : "";

  const meta = item.reference === "Pivot Central Range"
    ? (item.sessionDate ? `session ${esc(item.sessionDate)}` : "")
    : [
        item.lastBarWindowsStartTime ? `last bar ${esc(clockTime(item.lastBarWindowsStartTime))}` : null,
        item.atr ? `ATR ${formatIndicatorValue(item.atr)}` : null,
      ].filter(Boolean).join(" · ");

  return `
    <div class="indicator-row">
      <div class="candle-row-head">
        <div>
          <span class="candle-row-name" style="color:${instrumentColorVar(item.instrument)}">${esc(item.instrument)}</span>
          <span class="candle-tf">${esc(item.timeframe)}</span>
        </div>
        <div>
          ${directionBadge}
          <span class="badge status-${item.status}">${esc(label)}</span>
        </div>
      </div>
      <div class="indicator-body">
        <span class="indicator-ref">${refLabel}</span>
        <span class="indicator-value">${item.isSeeded ? formatIndicatorValue(item.value) : "—"}</span>
      </div>
      ${seedBar}
      ${meta ? `<div class="candle-row-foot"><span class="candle-row-meta">${meta}</span></div>` : ""}
    </div>
  `;
}

async function loadIndicatorsStatus() {
  const el = document.getElementById("indicators-status");
  try {
    const items = await fetchJson("/api/indicators");

    el.innerHTML = items.length
      ? items.map(indicatorCardHtml).join("")
      : `<div class="hint">No indicators seeded yet — WarmUpService seeds these at NSE's Init (09:00 IST), or run it manually (<code>POST /api/warmup/run</code> on warmup-live).</div>`;
  } catch (err) {
    el.innerHTML = `<div class="error">Unable to load: ${err.message}</div>`;
  }
}

async function refresh() {
  await Promise.all([
    loadServices(),
    loadCountryStatus(),
    loadExchangeTimelines(),
    loadIngestionStatus(),
    loadAggregationStatus(),
    loadIndicatorsStatus(),
    loadRuleEngine(),
  ]);
  applyPhase();
  document.getElementById("stamp").title = `Data last refreshed ${new Date().toLocaleTimeString()}`;
}

window.addEventListener("resize", () => drawConnections(lastServices));

refresh();
setInterval(refresh, REFRESH_MS);

// --- Strategy page (talks to strategy-live's published port directly from the browser) ---

// Fixed choices per the current spec. Exchange/Risk are hardcoded outright (shown disabled);
// the rest are closed lists for now — expand these arrays as the product grows.
const HARDCODED_EXCHANGE = "NSE";
const HARDCODED_RISK = "Moderate";
const GOALS_OPTIONS = ["Second Income", "Education Goal", "Retirement Goal", "Accumulation Goal"];
const BROKER_OPTIONS = ["Zerodha", "Upstox"];
const INSTRUMENT_OPTIONS = ["Bank Nifty Futures", "Bank Nifty Options", "Nifty 50 Futures", "Nifty 50 Options"];
const TRADETYPE_OPTIONS = ["Intraday", "CarryOver"];
const MONEYNESS_OPTIONS = ["ITM", "ATM", "OTM"];

// Which rule-level Properties.Instrument values are offered depends on which underlying is picked
// at the top: choosing a Nifty 50 instrument restricts every nested rule's Instrument field to
// Nifty-only values, and vice versa for Bank Nifty — so a strategy can't accidentally mix underlyings
// down in its rules.
const NIFTY_RULE_INSTRUMENTS = ["Nifty_Index_Spot", "Nifty_Future", "Nifty_Option"];
const BANKNIFTY_RULE_INSTRUMENTS = ["BankNifty_Index_Spot", "BankNifty_Future", "BankNifty_Option"];

function checkedInstruments() {
  const el = document.getElementById("f-instruments");
  return el ? Array.from(el.querySelectorAll("input:checked")).map(i => i.value) : [];
}

function allowedRuleInstruments() {
  const chosen = checkedInstruments();
  const hasNifty = chosen.some(i => i.includes("Nifty 50"));
  const hasBankNifty = chosen.some(i => i.includes("Bank Nifty"));
  if (hasNifty && !hasBankNifty) return NIFTY_RULE_INSTRUMENTS;
  if (hasBankNifty && !hasNifty) return BANKNIFTY_RULE_INSTRUMENTS;
  // nothing chosen yet, or both underlyings chosen at once — don't block editing, offer everything
  return [...NIFTY_RULE_INSTRUMENTS, ...BANKNIFTY_RULE_INSTRUMENTS];
}

function hasOptionsInstrumentChecked() {
  return checkedInstruments().some(i => i.includes("Options"));
}

function hasInstrumentOptions(instruments) {
  return !!(instruments && instruments.some(i => i.includes("Options")));
}

let strategyList = [];

// Working set of TradingSessionRules while the form is open (camelCase, matching what the API's
// GET returns — converted to PascalCase only at save time, in ruleToPascalCase). Structural edits
// (add/remove a rule) re-render this section from the array; field-level edits are read straight
// from the DOM at sync time rather than kept in sync on every keystroke, so typing never loses focus.
// Working state for every rule-array editor on the currently-open form, keyed by section id
// (e.g. "tradingSessionRules", "longEntryRules", "longEntryRisk", ...). Same TradingRule[] shape
// backs TradingSessionRules and all four EntryExitRule arrays, so one component serves all of them.
let ruleSections = {};

const OPERAND_TYPES = ["Indicator", "Literal", "Expression"];
const OPERATORS = ["==", "!=", "<", "<=", ">", ">="];
const LINK_OPTIONS = ["", "AND", "OR"];

// Every indicator type this stack actually has a live calculator for (StrategyService's
// RuleEvaluator.cs — one ClassifySource/ResolveIndicatorAsync branch per name here). An Indicator
// operand's name is picked from this list, not typed — a hand-typed name with no evaluator behind
// it would just silently sit unresolved forever with no obvious reason why. Period/Multiplier/
// Timeframe/Instrument stay free-form: those are per-instance parameters (which EMA period, which
// underlying), not a fixed vocabulary the way the indicator name itself is.
const INDICATOR_NAMES = ["EMA", "Supertrend", "Adaptive Supertrend", "Pivot Central Range"];

function esc(value) {
  return String(value ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

function emptyOperand(type) {
  return { type: type || "Literal", value: "", properties: null };
}

function emptyRule() {
  return { sequence: 1, leftOperand: emptyOperand("Indicator"), operator: "==", rightOperand: emptyOperand("Literal"), link: "" };
}

function instrumentOptionsHtml(currentValue) {
  const allowed = allowedRuleInstruments();
  // an out-of-set value (e.g. left over from before the top-level Instruments selection changed)
  // is kept as an extra option rather than silently dropped — no data loss, just visible as odd-one-out
  const options = !currentValue || allowed.includes(currentValue) ? allowed : [...allowed, currentValue];
  return `<option value="">(select)</option>` +
    options.map(v => `<option value="${esc(v)}" ${v === currentValue ? "selected" : ""}>${esc(v)}</option>`).join("");
}

// Indicator's name comes from INDICATOR_NAMES — an out-of-set value (e.g. a strategy authored
// before some indicator existed, or hand-edited) is kept as an extra option rather than silently
// dropped, same "don't lose data, just show the odd-one-out" rule instrumentOptionsHtml already
// follows below.
function indicatorNameOptionsHtml(currentValue) {
  const options = !currentValue || INDICATOR_NAMES.includes(currentValue) ? INDICATOR_NAMES : [...INDICATOR_NAMES, currentValue];
  return `<option value="">(select)</option>` +
    options.map(v => `<option value="${esc(v)}" ${v === currentValue ? "selected" : ""}>${esc(v)}</option>`).join("");
}

function operandCardHtml(side, operand) {
  const o = operand || emptyOperand();
  const p = o.properties || {};
  const hideProps = o.type === "Literal";
  const isIndicator = o.type === "Indicator";
  return `
    <div class="operand-card" data-side="${side}">
      <div class="operand-label">${side === "left" ? "Left Operand" : "Right Operand"}</div>
      <div class="operand-type-row">
        <select class="operand-type-select">
          ${OPERAND_TYPES.map(t => `<option value="${t}" ${o.type === t ? "selected" : ""}>${t}</option>`).join("")}
        </select>
        <select class="operand-value-select" ${isIndicator ? "" : "hidden"}>${indicatorNameOptionsHtml(o.value)}</select>
        <input type="text" class="operand-value-input" ${isIndicator ? "hidden" : ""} value="${esc(o.value)}" placeholder="e.g. Allocated Capital / 0.02 * Allocated Capital">
      </div>
      <div class="operand-props-grid" ${hideProps ? "hidden" : ""}>
        <input type="number" class="prop-period" placeholder="Period" value="${p.period ?? ""}">
        <input type="number" class="prop-multiplier" placeholder="Multiplier" value="${p.multiplier ?? ""}">
        <input type="text" class="prop-timeframe" placeholder="Timeframe e.g. 5 Minutes" value="${esc(p.timeframe ?? "")}">
        <select class="prop-instrument" title="Constrained to the underlying(s) chosen in Instruments above">${instrumentOptionsHtml(p.instrument ?? "")}</select>
        <select class="prop-relpos">
          <option value="" ${!p.relativePosition ? "selected" : ""}>Current</option>
          <option value="Previous" ${p.relativePosition === "Previous" ? "selected" : ""}>Previous</option>
        </select>
      </div>
    </div>
  `;
}

function ruleRowHtml(rule, index, key) {
  return `
    <div class="rule-row" data-key="${key}" data-index="${index}">
      <div class="rule-row-head">
        <span class="rule-row-title">Rule ${index + 1}</span>
        <button type="button" class="btn btn-danger rule-remove-btn" data-key="${key}" data-index="${index}">Remove</button>
      </div>
      <div class="rule-row-grid">
        ${operandCardHtml("left", rule.leftOperand)}
        <div class="operand-card operator-card">
          <div class="operand-label">Operator</div>
          <select class="rule-operator-select">
            ${OPERATORS.map(op => `<option value="${op}" ${rule.operator === op ? "selected" : ""}>${esc(op)}</option>`).join("")}
          </select>
        </div>
        ${operandCardHtml("right", rule.rightOperand)}
      </div>
      <div class="rule-link-row">
        <label>Link to next rule:</label>
        <select class="rule-link-select">
          ${LINK_OPTIONS.map(l => `<option value="${l}" ${rule.link === l ? "selected" : ""}>${l || "(none — last rule)"}</option>`).join("")}
        </select>
      </div>
    </div>
  `;
}

// One <div class="rules-section"> block: a header with an "+ Add Rule" button and a list container.
// `key` must match what ruleSections[] is keyed by and what the Add/Remove buttons carry in data-key.
function ruleSectionBlockHtml(key, title, titleClass = "rules-section-title") {
  return `
    <div class="rules-section">
      <div class="rules-section-head">
        <div class="${titleClass}">${esc(title)}</div>
        <button type="button" class="btn rule-add-btn" data-key="${key}">+ Add Rule</button>
      </div>
      <div class="rule-list" data-key="${key}"></div>
    </div>
  `;
}

function renderRuleSection(key) {
  const container = document.querySelector(`.rule-list[data-key="${key}"]`);
  if (!container) return;
  const rules = ruleSections[key] || [];
  container.innerHTML = rules.length
    ? rules.map((r, i) => ruleRowHtml(r, i, key)).join("")
    : `<div class="hint">No rules yet — click "Add Rule".</div>`;
}

function readOperandFromCard(card) {
  const type = card.querySelector(".operand-type-select").value;
  const value = type === "Indicator"
    ? card.querySelector(".operand-value-select").value.trim()
    : card.querySelector(".operand-value-input").value.trim();

  if (type === "Literal") {
    return { type, value, properties: null };
  }

  const period = card.querySelector(".prop-period").value;
  const multiplier = card.querySelector(".prop-multiplier").value;
  const timeframe = card.querySelector(".prop-timeframe").value.trim();
  const instrument = card.querySelector(".prop-instrument").value.trim();
  const relativePosition = card.querySelector(".prop-relpos").value;
  const hasProps = period || multiplier || timeframe || instrument || relativePosition;

  return {
    type,
    value,
    properties: hasProps
      ? {
          period: period ? Number(period) : 0,
          multiplier: multiplier ? Number(multiplier) : 0,
          timeframe: timeframe || null,
          instrument: instrument || null,
          relativePosition: relativePosition || null,
        }
      : null,
  };
}

function syncRuleSectionFromDom(key) {
  const rows = document.querySelectorAll(`.rule-row[data-key="${key}"]`);
  ruleSections[key] = Array.from(rows).map((row, i) => ({
    sequence: i + 1,
    leftOperand: readOperandFromCard(row.querySelector('.operand-card[data-side="left"]')),
    operator: row.querySelector(".rule-operator-select").value,
    rightOperand: readOperandFromCard(row.querySelector('.operand-card[data-side="right"]')),
    link: row.querySelector(".rule-link-select").value,
  }));
}

function syncAllRuleSectionsFromDom() {
  Object.keys(ruleSections).forEach(syncRuleSectionFromDom);
}

function operandToPascalCase(o) {
  return {
    Type: o.type,
    Value: o.value,
    Properties: o.properties
      ? {
          Period: o.properties.period,
          Multiplier: o.properties.multiplier,
          Timeframe: o.properties.timeframe,
          Instrument: o.properties.instrument,
          RelativePosition: o.properties.relativePosition,
        }
      : null,
  };
}

function ruleToPascalCase(r) {
  return {
    Sequence: r.sequence,
    LeftOperand: operandToPascalCase(r.leftOperand),
    Operator: r.operator,
    RightOperand: operandToPascalCase(r.rightOperand),
    Link: r.link,
  };
}

function describeOperand(o) {
  if (!o) return "—";
  let s = o.value || "(empty)";
  if (o.type !== "Literal" && o.properties) {
    const bits = [];
    if (o.properties.period) bits.push(`P${o.properties.period}`);
    if (o.properties.multiplier) bits.push(`x${o.properties.multiplier}`);
    if (o.properties.timeframe) bits.push(o.properties.timeframe);
    if (o.properties.instrument) bits.push(o.properties.instrument);
    if (o.properties.relativePosition) bits.push(o.properties.relativePosition);
    if (bits.length) s += ` (${bits.join(", ")})`;
  }
  return esc(s);
}

function describeRule(r) {
  return `${describeOperand(r.leftOperand)} <b>${esc(r.operator)}</b> ${describeOperand(r.rightOperand)}` +
    (r.link ? ` <span class="rule-link-tag">${esc(r.link)}</span>` : "");
}

function ruleListReadonlyHtml(title, rules, titleClass = "rules-section-title") {
  return `
    <div class="rules-section">
      <div class="${titleClass}">${esc(title)}</div>
      ${rules && rules.length
        ? rules.map(r => `<div class="rule-readonly">${describeRule(r)}</div>`).join("")
        : `<div class="hint">No rules defined.</div>`}
    </div>
  `;
}

/* ── Rule Engine page ────────────────────────────────────────────────────── */

const RULE_STATUS_LABELS = { pass: "Pass", fail: "Fail", unknown: "Can't evaluate" };

/* ── "Focus" redesign ────────────────────────────────────────────────────
   Replaces the old always-show-everything spine (every gate, both sides, entry AND exit, plus an
   interactive source rail with per-rule evidence drawers) with a single-branch view: pick a side
   (Long/Short) and a lens (Entry/Exit/Risk), and only that branch's chain renders. The trade-off
   is real — the old page could answer "what does the whole tree look like right now" at a glance;
   this one answers "is THIS path armed" faster, at the cost of the others being a click away. The
   interactive evidence-drawer/hover-linking system is gone with it, not hidden — a "Reads" strip
   at the bottom of whatever's on screen replaces it with the plain current values, no drill-down.
   See design-system/algotrading-dashboard/pages/rule-engine.md. */

let ruleEngineLastPayload = null;

// One side of a comparison, inline rather than stacked — "Supertrend 24,438.76 < EMA P550
// 24,505.93" reads left to right the way the rule itself does. describeOperand() still carries
// the period/timeframe/instrument detail; only the layout changed from the old two-line card.
//
// A literal's "resolved value" is just its own name — describeOperand(Literal "RED") already
// prints "RED", so appending its value too would print "RED RED". Checked on the operand's own
// type, not evidence.kind: a never-evaluated rule's evidence is always Kind "unresolved" even for
// a literal (NotEvaluated skips resolving anything), which would otherwise show a "—" placeholder
// implying a literal is missing data it was never going to need in the first place.
function spineCompareHtml(rule, left, right) {
  const val = (operand, ev) => {
    if (!operand || operand.type === "Literal") return "";
    return ev && ev.display ? `<span class="spine-val">${esc(ev.display)}</span>` : `<span class="spine-val none">—</span>`;
  };
  return `${describeOperand(rule.leftOperand)} ${val(rule.leftOperand, left)} <b>${esc(rule.operator || "")}</b> ${val(rule.rightOperand, right)} ${describeOperand(rule.rightOperand)}`;
}

// Reads the ATR off whichever side carries it (only Supertrend does). ATR is the one real
// volatility unit available here, so it's the only scale the gap bar is ever drawn against — with
// no ATR there's no honest scale, and the bar is simply omitted rather than invented.
function atrFrom(...evidences) {
  for (const ev of evidences) {
    for (const f of (ev && ev.fields) || []) {
      if (f.key === "Atr") {
        const n = Number(f.value);
        if (Number.isFinite(n) && n > 0) return n;
      }
    }
  }
  return null;
}

// The distance-to-flipping phrase — "passing by 67.17", "12.40 away from passing" — shared by the
// spine node detail line and the compact Gates Cleared checklist summary. Text only; ruleGapHtml
// below adds the ATR bar on top of this for the spine's own detail line.
function ruleGapPhrase(ev) {
  const l = ev.left && ev.left.numeric, r = ev.right && ev.right.numeric;
  if (typeof l !== "number" || typeof r !== "number") return null;

  const delta = Math.abs(l - r);
  const fmt = delta.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const op = (ev.rule.operator || "").trim();
  const equality = op === "==" || op === "!=";

  if (ev.status === "pass") return equality ? (delta === 0 ? "exactly equal" : `${fmt} apart`) : `passing by ${fmt}`;
  if (ev.status === "fail") return equality ? `${fmt} apart` : `${fmt} away from passing`;
  return `${fmt} apart`;
}

// Only drawn when both sides genuinely resolved to numbers — a text comparison
// (Supertrend == GREEN) has no meaningful "gap", and an unresolved side has no number at all.
function ruleGapHtml(ev) {
  const phrase = ruleGapPhrase(ev);
  if (phrase == null) return "";

  const delta = Math.abs(ev.left.numeric - ev.right.numeric);
  const fmt = delta.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const atr = atrFrom(ev.left, ev.right);
  let track = "";
  if (atr) {
    const multiples = delta / atr;
    const pct = Math.min(multiples / 3, 1) * 100;
    track = `
      <div class="gap-track" title="Scale: 0 to 3× ATR (${atr.toLocaleString(undefined, { maximumFractionDigits: 2 })}), from the same Supertrend hash">
        <div class="gap-fill ${ev.status}" style="width:${pct.toFixed(1)}%"></div>
      </div>
      <span class="gap-scale">${multiples.toFixed(2)}× ATR</span>`;
  }

  return `<span class="gap-delta ${ev.status}">Δ ${fmt}</span><span class="gap-phrase">${esc(phrase)}</span>${track}`;
}

// "2026-08-10T15:25:00" -> "15:25". Only ever the time-of-day part shown, never a fabricated
// "flipped at" moment this stack has no way to actually detect (only the current running state
// is kept, not a change history) — this is honestly "the bar this value is as of", not "when it
// last changed".
function formatAsOf(raw) {
  const m = /T(\d{2}:\d{2})/.exec(raw || "");
  return m ? m[1] : (raw || "");
}

// One line under a spine node: the numeric gap+bar for a numeric comparison, an honest
// as-of-timestamped match/no-match for a text one (Supertrend color vs a Literal), or the
// unresolved reason when there's nothing to compare at all.
function spineDetailHtml(ev) {
  if (ev.status === "unknown") {
    return `<div class="spine-detail muted">${esc(ev.reason || "not evaluated")}</div>`;
  }

  const gap = ruleGapHtml(ev);
  if (gap) return `<div class="spine-detail">${gap}</div>`;

  const asOf = (ev.left && ev.left.asOf) || (ev.right && ev.right.asOf);
  const phrase = ev.status === "pass" ? "Matched exactly" : "Did not match";
  return `<div class="spine-detail">${esc(phrase)}${asOf ? ` · as of ${esc(formatAsOf(asOf))}` : ""}</div>`;
}

function spineNodeHtml(ev, key) {
  return `
    <div class="spine-node ${ev.status}" data-rule-key="${esc(key)}">
      <span class="spine-dot ${ev.status}"></span>
      <div class="spine-body">
        <div class="spine-head">
          <span class="spine-text">${spineCompareHtml(ev.rule, ev.left, ev.right)}</span>
          <span class="badge status-${ev.status}">${RULE_STATUS_LABELS[ev.status] || ev.status}</span>
        </div>
        ${spineDetailHtml(ev)}
      </div>
    </div>`;
}

// items: [{ev, linkAfter}] — linkAfter is the label ("AND"/"OR"/"THEN") drawn between this node
// and the next, or null after the last one. "THEN" marks a transition between two semantically
// different groups chained on one spine (Entry rules -> Risk sizing, see focusPanelHtml) rather
// than the rule's own Link, which only ever means AND/OR within a single group.
function spineFromItemsHtml(items) {
  if (!items.length) return `<div class="hint">No rules defined.</div>`;
  return `<div class="spine">${items.map((item, i) => {
    const node = spineNodeHtml(item.ev, item.key ?? i);
    const link = item.linkAfter ? `<div class="spine-link">${esc(item.linkAfter)}</div>` : "";
    return node + link;
  }).join("")}</div>`;
}

function chainWithLinks(rules, scope, fallbackLink, trailingLink) {
  return rules.map((ev, i) => ({
    ev,
    key: `${scope}:${ev.rule.sequence ?? i}`,
    linkAfter: i < rules.length - 1 ? ((ev.rule.link || "").trim().toUpperCase() || fallbackLink) : (trailingLink || null),
  }));
}

// The Entry lens shows more than just Entry Rules: whether an armed entry can actually be SIZED
// (Risk Management) is part of "would this trade happen", not a separate question, so the two
// groups chain on one spine with a "THEN" transition rather than living in different tabs.
function combinedSpineForEntry(entryExit, side) {
  const entry = chainWithLinks(entryExit.entryRules.rules, `${side}:entry`, "AND",
    entryExit.riskManagementRules.rules.length ? "THEN" : null);
  const risk = chainWithLinks(entryExit.riskManagementRules.rules, `${side}:risk`, "AND", null);
  return entry.concat(risk);
}

const TAB_LABELS = { entry: "Entry", exit: "Exit", risk: "Risk" };

function focusItemsFor(entryExit, side, tab) {
  if (tab === "entry") return { items: combinedSpineForEntry(entryExit, side), group: entryExit.entryRules };
  if (tab === "risk") return { items: chainWithLinks(entryExit.riskManagementRules.rules, `${side}:risk`, "AND", null), group: entryExit.riskManagementRules };
  return { items: chainWithLinks(entryExit.exitBranch.rules, `${side}:exit`, "OR", null), group: entryExit.exitBranch };
}

// "N of M met" only ever counts rules that actually resolved to pass/fail — an unknown rule
// (Risk Management, always unevaluated) isn't a miss, it's a question this stack can't answer yet,
// so it's shown on the spine but left out of the ratio entirely rather than counted against it.
function metPillHtml(group) {
  if (!group.live) return `<span class="met-pill unknown">Not evaluated</span>`;

  const evaluated = group.rules.filter(r => r.status === "pass" || r.status === "fail");
  if (!evaluated.length) return `<span class="met-pill unknown">No rules to evaluate</span>`;

  const met = evaluated.filter(r => r.status === "pass").length;
  const dots = evaluated.map(r => `<span class="met-dot ${r.status}"></span>`).join("");
  return `<span class="met-pill"><span class="met-dots">${dots}</span>${met} of ${evaluated.length} met</span>`;
}

function sideDotTone(entryExit) {
  const s = entryExit && entryExit.entryRules && entryExit.entryRules.status;
  return s === "pass" ? "pass" : s === "fail" ? "fail" : "unknown";
}

function sideToggleHtml(data, side) {
  return `
    <div class="side-toggle" role="tablist">
      <button type="button" class="side-toggle-btn ${side === "long" ? "active" : ""}" data-side="long">
        <span class="side-dot ${sideDotTone(data.long)}"></span>Long
      </button>
      <button type="button" class="side-toggle-btn ${side === "short" ? "active" : ""}" data-side="short">
        <span class="side-dot ${sideDotTone(data.short)}"></span>Short
      </button>
    </div>`;
}

function tabToggleHtml(tab) {
  return `
    <div class="tab-toggle" role="tablist">
      ${Object.entries(TAB_LABELS).map(([key, label]) =>
        `<button type="button" class="tab-toggle-btn ${tab === key ? "active" : ""}" data-tab="${key}">${label}</button>`
      ).join("")}
    </div>`;
}

// The other half of what's on screen: not "would this pass" but "is any of it real right now".
// Scoped to only the inputs the currently-visible spine actually reads — the full inventory (every
// input across every side/lens) lived in the old source rail; this is deliberately just what's in
// front of you, since that's the question a compact strip at the bottom of one view can answer.
function readsStripHtml(sources, items) {
  const ids = new Set();
  items.forEach(item => (item.ev.sourceIds || []).forEach(id => ids.add(id)));
  const list = (sources || []).filter(s => ids.has(s.id));
  if (!list.length) return "";

  return `
    <div class="reads-strip">
      <div class="reads-label">Reads</div>
      <div class="reads-list">
        ${list.map(s => `
          <div class="read-tile">
            <div class="read-label">${esc(s.label)}</div>
            <div class="read-value ${s.backed ? "" : "none"}">${s.backed ? esc(s.value) : "no source"}</div>
          </div>`).join("")}
      </div>
    </div>`;
}

function focusPanelHtml(data, side, tab) {
  const entryExit = data[side];
  const { items, group } = focusItemsFor(entryExit, side, tab);

  return `
    <div class="focus-toolbar">
      ${sideToggleHtml(data, side)}
      ${tabToggleHtml(tab)}
      ${metPillHtml(group)}
    </div>
    ${spineFromItemsHtml(items)}
    ${readsStripHtml(data.sources, items)}`;
}

function gateChecklistIcon(tone) {
  if (tone === "pass") return `<span class="gate-icon pass">✓</span>`;
  if (tone === "fail") return `<span class="gate-icon fail">✕</span>`;
  if (tone === "pointer") return `<span class="gate-icon pointer">↓</span>`;
  return `<span class="gate-icon unknown">?</span>`;
}

function gateChecklistItemHtml(label, detail, tone) {
  return `
    <div class="gate-check-item">
      ${gateChecklistIcon(tone)}
      <div>
        <div class="gate-check-label">${esc(label)}</div>
        <div class="gate-check-detail">${esc(detail)}</div>
      </div>
    </div>`;
}

function dataHealthHtml(sources) {
  const list = sources || [];
  const backed = list.filter(s => s.backed).length;
  const total = list.length;
  const unbacked = total - backed;
  const pct = total ? (backed / total) * 100 : 0;

  return `
    <div class="data-health">
      <div class="health-head">
        <span class="health-title">Data health</span>
        <span class="health-count">${backed}/${total}</span>
      </div>
      <div class="health-bar"><div class="health-fill" style="width:${pct.toFixed(1)}%"></div></div>
      <div class="health-caption">${unbacked} input${unbacked === 1 ? "" : "s"} have no writer. Exit and risk rules can't evaluate until they do.</div>
    </div>`;
}

// The trading-session-rules gate's compact summary — reuses the same gap phrase the spine uses,
// so "passing by 113.89" means the same thing whether you're reading it in the checklist or the
// full rule row. Only handles the shapes this stack's strategies actually have (0 or 1 session
// rule); a strategy with several would just fall back to a plain status word, honestly.
function tradingSessionSummary(group) {
  if (!group.rules.length) return "No session rules configured";
  if (group.rules.length > 1) return `${group.rules.length} rules · ${RULE_STATUS_LABELS[group.status] || group.status}`;
  const ev = group.rules[0];
  if (ev.status === "unknown") return ev.reason || "not evaluated";
  return ruleGapPhrase(ev) || (ev.status === "pass" ? "Matched" : "Did not match");
}

function ruleEngineFocusHtml(data, sessionGate, side, tab) {
  const sessionState = (sessionGate.values && sessionGate.values[0] && sessionGate.values[0].value) || "—";
  const sessionDetail = sessionGate.status === "pass" ? `${sessionState} · gated open`
    : sessionGate.status === "fail" ? `${sessionState} — no session today`
    : "session state unavailable";

  return `
    <div class="identity">
      <div class="identity-name">${esc(data.strategyName)}</div>
      <div class="identity-meta">${esc((data.instruments || []).join(", "))}${data.exchange ? " · " + esc(data.exchange) : ""}${data.deployedVersion ? " · v" + esc(data.deployedVersion) : ""}</div>
    </div>

    <div class="focus-layout">
      <aside class="gates-rail">
        <div class="gates-rail-title">Gates cleared</div>
        <div class="gates-list">
          ${gateChecklistItemHtml("Session today", sessionDetail, sessionGate.status)}
          ${gateChecklistItemHtml("Trading session rules", tradingSessionSummary(data.tradingSessionRules), data.tradingSessionRules.status)}
          ${gateChecklistItemHtml("In a position", "Nothing tracks this — assumed flat", data.positionGate.status)}
          ${gateChecklistItemHtml(`${side === "long" ? "Long" : "Short"} branch`, `${TAB_LABELS[tab]} rules evaluating`, "pointer")}
        </div>
        ${dataHealthHtml(data.sources)}
      </aside>

      <div class="focus-main">
        ${focusPanelHtml(data, side, tab)}
      </div>
    </div>

    <div class="legend">
      <span><span class="dot pass"></span>Pass — a real live value satisfied the condition</span>
      <span><span class="dot fail"></span>Fail — a real live value did not satisfy it</span>
      <span><span class="dot unknown"></span>Can't evaluate — no data source exists for this yet</span>
      <span>The spine is the AND chain — read it top to bottom</span>
    </div>
  `;
}

// Which side/lens is on screen — module-level, like the strategy selection below, so a toggle
// click survives the 5s poll and a re-render can happen instantly from cached data with no
// network round-trip (renderRuleEngineContent below).
let ruleEngineSide = "long";
let ruleEngineTab = "entry";
let ruleEngineLastData = null;
let ruleEngineLastSessionGate = null;

function renderRuleEngineContent() {
  if (!ruleEngineLastData || !ruleEngineLastSessionGate) return;
  document.getElementById("rule-engine-content").innerHTML =
    ruleEngineFocusHtml(ruleEngineLastData, ruleEngineLastSessionGate, ruleEngineSide, ruleEngineTab);
}

// Toggle clicks are delegated off the page container so they survive the 5s refresh replacing
// the DOM underneath them — bound once at load, same contract as the strategy switcher below.
document.getElementById("rule-engine-content").addEventListener("click", ev => {
  const sideBtn = ev.target.closest(".side-toggle-btn");
  if (sideBtn && sideBtn.dataset.side !== ruleEngineSide) {
    ruleEngineSide = sideBtn.dataset.side;
    renderRuleEngineContent();
    return;
  }

  const tabBtn = ev.target.closest(".tab-toggle-btn");
  if (tabBtn && tabBtn.dataset.tab !== ruleEngineTab) {
    ruleEngineTab = tabBtn.dataset.tab;
    renderRuleEngineContent();
  }
});

// Which deployed strategy's tree is on screen — module-level so it survives the 5s poll cycle
// (the whole point is that switching strategies is a user action, not something a refresh should
// reset). Reconciled against the live deployed list on every load: if the selection deployed
// strategy disappears (undeployed/deleted), loadRuleEngine falls back to the first one available.
let ruleEngineSelectedId = null;
let ruleEngineSwitcherPayload = null;

function strategySwitcherHtml(deployedStrategies, selectedId) {
  if (deployedStrategies.length < 2) return ""; // one deployed strategy: a switcher has nothing to switch between
  return deployedStrategies.map(s => `
    <button type="button" class="strategy-chip ${s.id === selectedId ? "active" : ""}" data-strategy-id="${esc(s.id)}">
      <span class="chip-name">${esc(s.strategyName)}</span>
      <span class="chip-meta">${esc((s.instruments || []).join(", "))}</span>
      <span class="badge deployed">v${esc(s.deployedVersion || "—")}</span>
    </button>`).join("");
}

async function loadRuleEngine() {
  const el = document.getElementById("rule-engine-content");
  const errEl = document.getElementById("rule-engine-error");
  const switcherEl = document.getElementById("rule-engine-switcher");
  try {
    // Fetched together — the session gate is the same Redis "India" state no matter which
    // strategy ends up selected, so it doesn't wait on (or depend on) strategy selection at all.
    const [strategies, sessionGate] = await Promise.all([
      fetchJson(`${STRATEGY_API_BASE}/api/strategies`),
      fetchJson(`${STRATEGY_API_BASE}/api/session-status`),
    ]);
    const deployed = (strategies || []).filter(s => s.deployedVersion);

    if (deployed.length === 0) {
      el.innerHTML = `<div class="hint">No deployed strategy yet — deploy one from the Strategy page first.</div>`;
      ruleEngineLastPayload = null;
      ruleEngineLastData = null;
      ruleEngineLastSessionGate = null;
      switcherEl.hidden = true;
      switcherEl.innerHTML = "";
      ruleEngineSwitcherPayload = null;
      ruleEngineSelectedId = null;
      errEl.hidden = true;
      return;
    }

    // Stick with whatever the user picked as long as it's still deployed; otherwise (first load,
    // or the selected one just got undeployed) fall back to the first deployed strategy.
    if (!deployed.some(s => s.id === ruleEngineSelectedId)) {
      ruleEngineSelectedId = deployed[0].id;
    }

    const switcherPayload = JSON.stringify({ deployed: deployed.map(s => [s.id, s.strategyName, s.deployedVersion]), selected: ruleEngineSelectedId });
    if (switcherPayload !== ruleEngineSwitcherPayload) {
      ruleEngineSwitcherPayload = switcherPayload;
      switcherEl.innerHTML = strategySwitcherHtml(deployed, ruleEngineSelectedId);
      switcherEl.hidden = deployed.length < 2;
    }

    const status = await fetchJson(`${STRATEGY_API_BASE}/api/strategies/${encodeURIComponent(ruleEngineSelectedId)}/rule-status`);

    // The 5s refresh used to blow away and rebuild this whole subtree every tick, which made any
    // interaction on the page impossible to sustain — a side/tab selection would otherwise reset
    // every five seconds. Indicators only actually change on a bar close (5 minutes for this
    // strategy), so the overwhelming majority of ticks are identical payloads and now touch the
    // DOM not at all.
    const payload = JSON.stringify({ status, sessionGate });
    if (payload !== ruleEngineLastPayload) {
      ruleEngineLastPayload = payload;
      ruleEngineLastData = status;
      ruleEngineLastSessionGate = sessionGate;
      renderRuleEngineContent();
    }
    errEl.hidden = true;
  } catch (err) {
    errEl.hidden = false;
    errEl.textContent = `Unable to load: ${err.message}`;
  }
}

// Delegated the same way the toggle clicks are — bound once at load, survives the switcher being
// re-rendered on every strategy list change.
document.getElementById("rule-engine-switcher").addEventListener("click", ev => {
  const chip = ev.target.closest(".strategy-chip");
  if (!chip || chip.classList.contains("active")) return;

  ruleEngineSelectedId = chip.dataset.strategyId;
  ruleEngineLastPayload = null; // force the flow to redraw even if the new strategy's status JSON
                                 // happens to collide with whatever was cached from the last one
  loadRuleEngine();
});

function strategyError(message) {
  const el = document.getElementById("strategy-error");
  if (!message) {
    el.hidden = true;
    el.textContent = "";
    return;
  }
  el.hidden = false;
  el.textContent = message;
}

function closeStrategyPanel() {
  const panel = document.getElementById("strategy-panel");
  panel.hidden = true;
  panel.innerHTML = "";
}

async function loadStrategyGrid() {
  const el = document.getElementById("strategy-grid");
  try {
    const res = await fetch(`${STRATEGY_API_BASE}/api/strategies`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    strategyList = await res.json();
    strategyError(null);

    if (!strategyList.length) {
      el.innerHTML = `<div class="hint">No strategies yet — click "+ New Strategy" to create one, or drop a .json file into config/strategies/.</div>`;
      return;
    }

    el.innerHTML = `<div class="strategy-rows">${strategyList.map(strategyRowHtml).join("")}</div>`;
    el.querySelectorAll("[data-action]").forEach(btn => {
      btn.addEventListener("click", () => {
        const { action, id } = btn.dataset;
        if (action === "view") openStrategyView(id);
        else if (action === "edit") openStrategyEdit(id);
        else if (action === "deploy") deployStrategy(id);
        else if (action === "delete") deleteStrategyById(id);
      });
    });
  } catch (err) {
    el.innerHTML = "";
    strategyError(`Unable to reach strategy-live at ${STRATEGY_API_BASE} — is that service running? (${err.message})`);
  }
}

function strategyRowHtml(s) {
  const isDeployed = s.deployedVersion && s.deployedVersion === s.version;
  const deployBadge = s.deployedVersion
    ? (isDeployed
        ? `<span class="badge badge-deployed">Deployed v${esc(s.deployedVersion)}</span>`
        : `<span class="badge badge-not-deployed">Deployed v${esc(s.deployedVersion)} · behind</span>`)
    : `<span class="badge">Not deployed</span>`;

  // Only the facts that are actually set. The table this replaced rendered an
  // em-dash for every unset field, so a sparse strategy read as broken rather
  // than simply not configured yet.
  const meta = [
    esc(s.exchange || ""),
    esc(s.broker || ""),
    esc(s.tradeType || ""),
    s.risk ? `risk ${esc(s.risk)}` : "",
    s.goals && s.goals.length ? esc(s.goals.join(", ")) : "",
    s.instruments && s.instruments.length ? esc(s.instruments.join(", ")) : "",
  ].filter(Boolean).join(" · ");

  return `
    <div class="strategy-row">
      <div class="strategy-row-head">
        <div class="strategy-row-title">
          <span class="strategy-row-name">${esc(s.strategyName || s.id)}</span>
          <span class="candle-tf">v${esc(s.version)}</span>
          ${deployBadge}
        </div>
        <div class="strategy-row-actions">
          <button class="btn" data-action="view" data-id="${esc(s.id)}">View</button>
          <button class="btn" data-action="edit" data-id="${esc(s.id)}">Edit</button>
          <button class="btn btn-primary" data-action="deploy" data-id="${esc(s.id)}">Deploy</button>
          <button class="btn btn-danger" data-action="delete" data-id="${esc(s.id)}">Delete</button>
        </div>
      </div>
      ${meta ? `<div class="strategy-row-meta">${meta}</div>` : `<div class="strategy-row-meta">Nothing configured yet</div>`}
    </div>
  `;
}

async function fetchStrategy(id) {
  const res = await fetch(`${STRATEGY_API_BASE}/api/strategies/${encodeURIComponent(id)}`);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

async function openStrategyView(id) {
  try {
    renderStrategyViewPanel(id, await fetchStrategy(id));
  } catch (err) {
    strategyError(`Unable to load strategy '${id}': ${err.message}`);
  }
}

function renderStrategyViewPanel(id, s) {
  const sub = (s.strategies && s.strategies[0]) || {};
  const panel = document.getElementById("strategy-panel");
  panel.hidden = false;
  panel.innerHTML = `
    <div class="strategy-panel-head">
      <h3>${esc(s.strategyName || id)}</h3>
      <div class="strategy-panel-actions">
        <button class="btn" id="panel-edit-btn">Edit</button>
        <button class="btn" id="panel-close-btn">Close</button>
      </div>
    </div>
    <div class="form-grid">
      <div class="view-field"><div class="view-label">Exchange</div><div class="view-value">${esc(s.exchange)}</div></div>
      <div class="view-field"><div class="view-label">Version</div><div class="view-value">v${esc(s.version)}${s.deployedVersion ? ` (deployed: v${esc(s.deployedVersion)})` : " (not deployed)"}</div></div>
      <div class="view-field"><div class="view-label">Broker</div><div class="view-value">${esc(s.broker || "—")}</div></div>
      <div class="view-field"><div class="view-label">Risk</div><div class="view-value">${esc(sub.risk || "—")}</div></div>
      <div class="view-field"><div class="view-label">Trade Type</div><div class="view-value">${esc(sub.tradeType || "—")}</div></div>
      ${sub.moneyness ? `<div class="view-field"><div class="view-label">Moneyness</div><div class="view-value">${esc(sub.moneyness)}</div></div>` : ""}
      <div class="view-field span-2"><div class="view-label">Goals</div><div class="chip-row">${(s.goals || []).map(g => `<span class="chip">${esc(g)}</span>`).join("") || "—"}</div></div>
      <div class="view-field span-2"><div class="view-label">Instruments</div><div class="chip-row">${(sub.instruments || []).map(i => `<span class="chip">${esc(i)}</span>`).join("") || "—"}</div></div>
    </div>
    ${ruleListReadonlyHtml("Trading Session Rules", sub.tradingSessionRules, "rules-group-label rules-group-label--highlight")}
    <div class="rules-group-label rules-group-label--highlight">Long Entry</div>
    ${LONG_ENTRY_SECTIONS.map(([, title, sourceField]) => ruleListReadonlyHtml(title, (sub.longEntry || {})[sourceField])).join("")}
    <div class="rules-group-label rules-group-label--highlight">Short Entry</div>
    ${SHORT_ENTRY_SECTIONS.map(([, title, sourceField]) => ruleListReadonlyHtml(title, (sub.shortEntry || {})[sourceField])).join("")}
  `;
  document.getElementById("panel-edit-btn").addEventListener("click", () => openStrategyEdit(id));
  document.getElementById("panel-close-btn").addEventListener("click", closeStrategyPanel);
}

async function openStrategyEdit(id) {
  try {
    renderStrategyForm(id, await fetchStrategy(id));
  } catch (err) {
    strategyError(`Unable to load strategy '${id}': ${err.message}`);
  }
}

function openStrategyNew() {
  renderStrategyForm(null, null);
}

// Section keys used throughout: which EntryExitRule sub-array each maps to, on which side.
// Titles don't repeat "Long Entry:"/"Short Entry:" — that's the enclosing
// .rules-group-label--highlight's job (see renderStrategyViewPanel /
// renderStrategyForm), and repeating it here is what these titles used to do.
const LONG_ENTRY_SECTIONS = [
  ["longEntryRules", "Entry Rules", "entryRules"],
  ["longEntryRisk", "Risk Management Rules", "riskManagementRules"],
  ["longEntryStopLoss", "Update Stop-Loss Rules", "updateStopLossRules"],
  ["longEntryExit", "Exit Rules", "exitRules"],
];
const SHORT_ENTRY_SECTIONS = [
  ["shortEntryRules", "Entry Rules", "entryRules"],
  ["shortEntryRisk", "Risk Management Rules", "riskManagementRules"],
  ["shortEntryStopLoss", "Update Stop-Loss Rules", "updateStopLossRules"],
  ["shortEntryExit", "Exit Rules", "exitRules"],
];

function renderStrategyForm(id, existing) {
  const isNew = !existing;
  const sub = (existing && existing.strategies && existing.strategies[0]) || {};
  const longEntry = (!isNew && sub.longEntry) || {};
  const shortEntry = (!isNew && sub.shortEntry) || {};

  // deep-cloned (not a live reference into `sub`) so Cancel doesn't leave mutated state behind
  const clone = v => JSON.parse(JSON.stringify(v || []));
  ruleSections = { tradingSessionRules: isNew ? [] : clone(sub.tradingSessionRules) };
  LONG_ENTRY_SECTIONS.forEach(([key, , sourceField]) => {
    ruleSections[key] = isNew ? [] : clone(longEntry[sourceField]);
  });
  SHORT_ENTRY_SECTIONS.forEach(([key, , sourceField]) => {
    ruleSections[key] = isNew ? [] : clone(shortEntry[sourceField]);
  });

  const panel = document.getElementById("strategy-panel");
  panel.hidden = false;
  panel.innerHTML = `
    <div class="strategy-panel-head">
      <h3>${isNew ? "New Strategy" : `Edit: ${esc(existing.strategyName || id)}`}</h3>
      <div class="strategy-panel-actions">
        <button class="btn btn-primary" id="form-save-btn">Save</button>
        <button class="btn" id="form-cancel-btn">Cancel</button>
      </div>
    </div>
    <div class="form-grid">
      <div class="form-field">
        <label>Exchange</label>
        <input type="text" value="${esc(HARDCODED_EXCHANGE)}" disabled>
      </div>
      <div class="form-field">
        <label>Strategy Name</label>
        <input type="text" id="f-name" value="${esc(isNew ? "" : existing.strategyName)}" placeholder="e.g. Second Income">
      </div>
      <div class="form-field">
        <label>Version</label>
        <input type="text" value="${isNew ? "will be 1.0.0" : `v${esc(existing.version)} (auto-bumps on save)`}" disabled>
      </div>
      <div class="form-field">
        <label>Broker</label>
        <select id="f-broker">
          ${BROKER_OPTIONS.map(b => `<option value="${esc(b)}" ${existing && existing.broker === b ? "selected" : ""}>${esc(b)}</option>`).join("")}
        </select>
      </div>
      <div class="form-field span-2">
        <label>Goals</label>
        <div class="checkbox-group" id="f-goals">
          ${GOALS_OPTIONS.map(g => `<label><input type="checkbox" value="${esc(g)}" ${existing && existing.goals && existing.goals.includes(g) ? "checked" : ""}> ${esc(g)}</label>`).join("")}
        </div>
      </div>
      <div class="form-field">
        <label>Risk</label>
        <input type="text" value="${esc(HARDCODED_RISK)}" disabled>
      </div>
      <div class="form-field">
        <label>Trade Type</label>
        <select id="f-tradetype">
          ${TRADETYPE_OPTIONS.map(t => `<option value="${esc(t)}" ${sub.tradeType === t ? "selected" : ""}>${esc(t)}</option>`).join("")}
        </select>
      </div>
      <div class="form-field span-2">
        <label>Instruments</label>
        <div class="checkbox-group" id="f-instruments">
          ${INSTRUMENT_OPTIONS.map(i => `<label><input type="checkbox" value="${esc(i)}" ${sub.instruments && sub.instruments.includes(i) ? "checked" : ""}> ${esc(i)}</label>`).join("")}
        </div>
      </div>
      <div class="form-field" id="f-moneyness-field" ${hasInstrumentOptions(sub.instruments) ? "" : "hidden"}>
        <label>Moneyness</label>
        <select id="f-moneyness">
          ${MONEYNESS_OPTIONS.map(m => `<option value="${esc(m)}" ${sub.moneyness === m ? "selected" : ""}>${esc(m)}</option>`).join("")}
        </select>
      </div>
    </div>

    ${ruleSectionBlockHtml("tradingSessionRules", "Trading Session Rules", "rules-group-label rules-group-label--highlight")}

    <div class="rules-group-label rules-group-label--highlight">Long Entry</div>
    ${LONG_ENTRY_SECTIONS.map(([key, title]) => ruleSectionBlockHtml(key, title)).join("")}

    <div class="rules-group-label rules-group-label--highlight">Short Entry</div>
    ${SHORT_ENTRY_SECTIONS.map(([key, title]) => ruleSectionBlockHtml(key, title)).join("")}

    <div id="form-status" class="hint"></div>
  `;

  Object.keys(ruleSections).forEach(renderRuleSection);

  document.getElementById("form-cancel-btn").addEventListener("click", closeStrategyPanel);
  document.getElementById("form-save-btn").addEventListener("click", () => saveStrategyForm(id));
}

async function saveStrategyForm(id) {
  const status = document.getElementById("form-status");
  status.classList.remove("error");

  const name = document.getElementById("f-name").value.trim();
  if (!name) {
    status.classList.add("error");
    status.textContent = "Strategy name is required.";
    return;
  }

  const broker = document.getElementById("f-broker").value;
  const tradeType = document.getElementById("f-tradetype").value;
  const goals = Array.from(document.querySelectorAll("#f-goals input:checked")).map(i => i.value);
  const instruments = Array.from(document.querySelectorAll("#f-instruments input:checked")).map(i => i.value);
  const moneynessField = document.getElementById("f-moneyness");
  const moneyness = moneynessField && !document.getElementById("f-moneyness-field").hidden ? moneynessField.value : null;

  syncAllRuleSectionsFromDom();

  const targetId = id || name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "") || "strategy";

  // Version/DeployedVersion are deliberately omitted — strategy-live computes/preserves those server-side.
  const payload = {
    Exchange: HARDCODED_EXCHANGE,
    StrategyName: name,
    Broker: broker,
    Goals: goals,
    Strategies: [
      {
        Risk: HARDCODED_RISK,
        Instruments: instruments,
        Moneyness: moneyness,
        TradeType: tradeType,
        TradingSessionRules: ruleSections.tradingSessionRules.map(ruleToPascalCase),
        LongEntry: {
          EntryRules: ruleSections.longEntryRules.map(ruleToPascalCase),
          RiskManagementRules: ruleSections.longEntryRisk.map(ruleToPascalCase),
          UpdateStopLossRules: ruleSections.longEntryStopLoss.map(ruleToPascalCase),
          ExitRules: ruleSections.longEntryExit.map(ruleToPascalCase),
        },
        ShortEntry: {
          EntryRules: ruleSections.shortEntryRules.map(ruleToPascalCase),
          RiskManagementRules: ruleSections.shortEntryRisk.map(ruleToPascalCase),
          UpdateStopLossRules: ruleSections.shortEntryStopLoss.map(ruleToPascalCase),
          ExitRules: ruleSections.shortEntryExit.map(ruleToPascalCase),
        },
      },
    ],
  };

  status.textContent = "Saving…";
  try {
    const res = await fetch(`${STRATEGY_API_BASE}/api/strategies/${encodeURIComponent(targetId)}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    const body = await res.json();
    if (!res.ok) throw new Error(body.error || `HTTP ${res.status}`);
    closeStrategyPanel();
    loadStrategyGrid();
  } catch (err) {
    status.classList.add("error");
    status.textContent = `Save failed: ${err.message}`;
  }
}

async function deployStrategy(id) {
  try {
    const res = await fetch(`${STRATEGY_API_BASE}/api/strategies/${encodeURIComponent(id)}/deploy`, { method: "POST" });
    const body = await res.json();
    if (!res.ok) throw new Error(body.error || `HTTP ${res.status}`);
    loadStrategyGrid();
  } catch (err) {
    strategyError(`Deploy failed: ${err.message}`);
  }
}

async function deleteStrategyById(id) {
  if (!confirm(`Delete strategy "${id}"? This can't be undone.`)) return;
  try {
    const res = await fetch(`${STRATEGY_API_BASE}/api/strategies/${encodeURIComponent(id)}`, { method: "DELETE" });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    closeStrategyPanel();
    loadStrategyGrid();
  } catch (err) {
    strategyError(`Delete failed: ${err.message}`);
  }
}

document.getElementById("strategy-new").addEventListener("click", openStrategyNew);

// Delegated once on the static #strategy-panel container (present from page load) rather than
// re-wired inside renderStrategyForm on every render — the rule sections it contains are rebuilt
// via innerHTML each time the form opens, so per-render listeners would otherwise need re-attaching.
document.getElementById("strategy-panel").addEventListener("click", e => {
  const addBtn = e.target.closest(".rule-add-btn");
  if (addBtn) {
    const key = addBtn.dataset.key;
    syncRuleSectionFromDom(key);
    ruleSections[key].push(emptyRule());
    renderRuleSection(key);
    return;
  }

  const removeBtn = e.target.closest(".rule-remove-btn");
  if (removeBtn) {
    const key = removeBtn.dataset.key;
    syncRuleSectionFromDom(key);
    ruleSections[key].splice(Number(removeBtn.dataset.index), 1);
    renderRuleSection(key);
  }
});

document.getElementById("strategy-panel").addEventListener("change", e => {
  if (e.target.classList.contains("operand-type-select")) {
    const card = e.target.closest(".operand-card");
    const isIndicator = e.target.value === "Indicator";
    card.querySelector(".operand-props-grid").hidden = e.target.value === "Literal";
    // Switching Type changes what "value" even means (an indicator name vs. a literal/expression
    // string) — the two fields intentionally don't carry a value across that switch, same as the
    // props grid already doesn't try to preserve Period/Multiplier when switching to Literal.
    card.querySelector(".operand-value-select").hidden = !isIndicator;
    card.querySelector(".operand-value-input").hidden = isIndicator;
    return;
  }

  if (e.target.closest("#f-instruments")) {
    const moneynessField = document.getElementById("f-moneyness-field");
    if (moneynessField) moneynessField.hidden = !hasOptionsInstrumentChecked();

    // the underlying just changed — every rule section's Instrument dropdown offers a different
    // set of options now, so re-render all of them (after syncing so in-progress edits aren't lost)
    Object.keys(ruleSections).forEach(key => {
      syncRuleSectionFromDom(key);
      renderRuleSection(key);
    });
  }
});
// initial load, if the strategy grid is already the active page, is handled by showPage() above

/* ── Backtest page ────────────────────────────────────────────────────────
   Simulates a strategy's own rules bar-by-bar against real historical data — see
   StrategyService/Backtest/BacktestEngine.cs for the actual simulation. This page only ever POSTs
   on an explicit "Run Backtest" click; unlike every other page, it's never touched by the 5s
   refresh() cycle — a backtest is a real, potentially several-second server computation, not
   status to poll. */

// Just the strategy picker, populated on every visit to the page (same "don't show stale data from
// a previous visit" reasoning the Strategy page's own loadStrategyGrid() already follows) — running
// a backtest itself is the separate, explicit action below.
async function loadBacktestForm() {
  const select = document.getElementById("bt-strategy");
  const startInput = document.getElementById("bt-start");
  const endInput = document.getElementById("bt-end");

  // Default range: the last 30 calendar days — just a reasonable starting point to have something
  // pre-filled, not a claim that history actually goes back that far. The backtest itself is what
  // honestly reports whether the picked range has real data (see the insufficient-data path below).
  if (!endInput.value) {
    const today = new Date();
    const start = new Date(today);
    start.setDate(start.getDate() - 30);
    endInput.value = today.toISOString().slice(0, 10);
    startInput.value = start.toISOString().slice(0, 10);
  }

  try {
    const strategies = await fetchJson(`${STRATEGY_API_BASE}/api/strategies`);
    const current = select.value;
    select.innerHTML = (strategies || []).length
      ? strategies.map(s => `<option value="${esc(s.id)}">${esc(s.strategyName)}${s.deployedVersion ? "" : " (not deployed)"}</option>`).join("")
      : `<option value="">No strategies yet</option>`;
    if (current && [...select.options].some(o => o.value === current)) select.value = current;
    backtestError(null);
  } catch (err) {
    backtestError(`Unable to load strategy list: ${err.message}`);
  }
}

function backtestError(message) {
  const el = document.getElementById("backtest-error");
  if (!message) { el.hidden = true; el.textContent = ""; return; }
  el.hidden = false;
  el.textContent = message;
}

function btStatusHtml(icon, tone, title, detailHtml) {
  return `
    <div class="bt-status ${tone}">
      <span class="bt-status-icon ${tone}">${icon}</span>
      <div>
        <div class="bt-status-title">${esc(title)}</div>
        <div class="bt-status-detail">${detailHtml}</div>
      </div>
    </div>`;
}

function btStatTile(label, value, tone, sub) {
  return `
    <div class="bt-stat">
      <div class="bt-stat-label">${esc(label)}</div>
      <div class="bt-stat-value ${tone || ""}">${esc(String(value))}</div>
      ${sub ? `<div class="bt-stat-sub">${esc(sub)}</div>` : ""}
    </div>`;
}

function btFormatPoints(n) {
  const sign = n > 0 ? "+" : "";
  return `${sign}${Number(n).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

// "2026-07-24T11:19:00" -> "24 Jul, 11:19" — compact, still unambiguous within the kind of
// sub-year range a backtest is typically run over.
function btFormatDateTime(iso) {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  return d.toLocaleString(undefined, { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit", hour12: false });
}

function backtestStatsHtml(stats) {
  // decimal.MaxValue (all wins, zero losses) round-trips through JSON as a real but enormous
  // number — shown as ∞ rather than the literal digits, which is what it actually means here.
  const profitFactorDisplay = stats.profitFactor > 1e6 ? "∞" : stats.profitFactor;

  return `
    <div class="bt-stats-grid">
      ${btStatTile("Total trades", stats.totalTrades)}
      ${btStatTile("Win rate", `${stats.winRatePct}%`, stats.winRatePct >= 50 ? "pass" : "fail", `${stats.wins}W / ${stats.losses}L`)}
      ${btStatTile("Total points", btFormatPoints(stats.totalPoints), stats.totalPoints >= 0 ? "pass" : "fail")}
      ${btStatTile("Profit factor", profitFactorDisplay, stats.profitFactor >= 1 ? "pass" : "fail")}
      ${btStatTile("Avg win", btFormatPoints(stats.averageWinPoints), "pass")}
      ${btStatTile("Avg loss", btFormatPoints(stats.averageLossPoints), "fail")}
      ${btStatTile("Largest win", btFormatPoints(stats.largestWinPoints), "pass")}
      ${btStatTile("Largest loss", btFormatPoints(stats.largestLossPoints), "fail")}
      ${btStatTile("Max drawdown", `-${Math.abs(stats.maxDrawdownPoints).toFixed(2)}`, stats.maxDrawdownPoints > 0 ? "fail" : "")}
      ${btStatTile("Longest win streak", stats.longestWinStreak)}
      ${btStatTile("Longest loss streak", stats.longestLossStreak)}
    </div>`;
}

function backtestTradesHtml(trades) {
  if (!trades.length) return "";
  const rows = trades.map(t => `
    <tr>
      <td><span class="bt-side ${t.side.toLowerCase()}">${esc(t.side)}</span></td>
      <td class="num">${esc(btFormatDateTime(t.entryTime))}</td>
      <td class="num">${Number(t.entryPrice).toLocaleString(undefined, { minimumFractionDigits: 2 })}</td>
      <td class="num">${esc(btFormatDateTime(t.exitTime))}</td>
      <td class="num">${Number(t.exitPrice).toLocaleString(undefined, { minimumFractionDigits: 2 })}</td>
      <td class="num pnl ${t.pointsPnl >= 0 ? "pass" : "fail"}">${btFormatPoints(t.pointsPnl)}</td>
      <td class="reason">${esc(t.exitReason)}${t.forcedCloseAtPeriodEnd ? " <em>(open at period end)</em>" : ""}</td>
    </tr>`).join("");

  return `
    <div class="bt-trades-title">Trades (${trades.length})</div>
    <div class="bt-trades-scroll">
      <table class="bt-trade-table">
        <thead><tr><th>Side</th><th>Entry</th><th>Entry Price</th><th>Exit</th><th>Exit Price</th><th>Points</th><th>Exit reason</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>
    </div>`;
}

function backtestIndicatorLabel(series) {
  return `${series.reference} (P${series.period}${series.multiplier ? ` ×${series.multiplier}` : ""}, ${series.timeframe})`;
}

// The indicator's own per-candle series, for actually eyeballing a rule against real data — not
// just trusting what trade outcomes imply. Shown even when there are zero trades: that's exactly
// when this view earns its place, letting you see whether the indicator itself is behaving as
// expected before assuming the rule (or the engine) is wrong. Each series scrolls independently
// (a long range at a fine timeframe can run to thousands of rows) and highlights the exact row a
// GREEN/RED flip happens on, since that's easy to miss reading values alone.
function backtestIndicatorSeriesHtml(seriesList) {
  if (!seriesList || !seriesList.length) return "";

  return seriesList.map(series => {
    let prevDirection = null;
    const rows = series.points.map(p => {
      const isFlip = p.direction != null && prevDirection != null && p.direction !== prevDirection;
      if (p.direction != null) prevDirection = p.direction;
      const dirClass = p.direction === "GREEN" ? "up" : p.direction === "RED" ? "down" : "none";
      return `
        <tr class="${isFlip ? "flip" : ""}">
          <td class="num">${esc(btFormatDateTime(p.time))}</td>
          <td class="num">${Number(p.close).toLocaleString(undefined, { minimumFractionDigits: 2 })}</td>
          <td class="num">${p.value != null ? Number(p.value).toLocaleString(undefined, { minimumFractionDigits: 2 }) : "—"}</td>
          <td><span class="dir ${dirClass}">${p.direction ? esc(p.direction) : "—"}</span></td>
        </tr>`;
    }).join("");

    return `
      <div class="bt-indicator-block">
        <div class="bt-indicator-title">${esc(backtestIndicatorLabel(series))} — ${series.points.length} candles</div>
        <div class="bt-indicator-scroll">
          <table class="bt-indicator-table">
            <thead><tr><th>Time</th><th>Close</th><th>Value</th><th>Direction</th></tr></thead>
            <tbody>${rows}</tbody>
          </table>
        </div>
      </div>`;
  }).join("");
}

// One switch on BacktestResponse.status — every shape the backend can honestly report (see
// BacktestModels.cs) gets its own plain-language card; "completed" is the only one that also draws
// stats/a trade table, and even then only when at least one trade actually happened.
function renderBacktestResponse(data) {
  if (data.status === "insufficient-data") {
    const missing = (data.dataAvailability?.days || []).filter(d => !d.exists).map(d => d.date);
    const missingHtml = missing.length ? `<div class="bt-missing-days">${missing.map(d => `<span class="bt-missing-day">${esc(d)}</span>`).join("")}</div>` : "";
    return btStatusHtml("!", "warn", "Not enough historical data for this range", esc(data.message) + missingHtml);
  }

  if (data.status === "no-entry-rules" || data.status === "no-instrument") {
    return btStatusHtml("?", "warn", "Nothing to simulate", esc(data.message));
  }

  if (data.status === "error") {
    return btStatusHtml("✕", "fail", "Backtest couldn't run", esc(data.message));
  }

  const hasTrades = data.trades && data.trades.length > 0;
  const summary = hasTrades
    ? btStatusHtml("✓", data.stats.totalPoints >= 0 ? "pass" : "fail",
        `${data.instrument} · ${data.simulationTimeframe} bars · ${data.startDate.slice(0, 10)} → ${data.endDate.slice(0, 10)}`,
        esc(data.message))
    : btStatusHtml("○", "warn", "No trades", esc(data.message));

  return summary
    + (hasTrades ? backtestStatsHtml(data.stats) + backtestTradesHtml(data.trades) : "")
    + backtestIndicatorSeriesHtml(data.indicatorSeries);
}

async function runBacktest() {
  const strategyId = document.getElementById("bt-strategy").value;
  const startDate = document.getElementById("bt-start").value;
  const endDate = document.getElementById("bt-end").value;
  const resultsEl = document.getElementById("backtest-results");
  const runBtn = document.getElementById("bt-run");

  backtestError(null);
  if (!strategyId) return backtestError("Pick a strategy first.");
  if (!startDate || !endDate) return backtestError("Pick both a start and end date.");
  if (endDate < startDate) return backtestError("End date must be on or after the start date.");

  runBtn.disabled = true;
  runBtn.textContent = "Running…";
  resultsEl.innerHTML = `<div class="hint">Fetching historical data and simulating trades — this can take a few seconds for a longer range.</div>`;

  try {
    const res = await fetch(`${STRATEGY_API_BASE}/api/strategies/${encodeURIComponent(strategyId)}/backtest`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ startDate, endDate }),
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);
    resultsEl.innerHTML = renderBacktestResponse(data);
  } catch (err) {
    resultsEl.innerHTML = "";
    backtestError(`Backtest failed: ${err.message}`);
  } finally {
    runBtn.disabled = false;
    runBtn.textContent = "Run Backtest";
  }
}

document.getElementById("bt-run").addEventListener("click", runBacktest);
