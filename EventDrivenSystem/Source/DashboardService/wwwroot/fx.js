/* ============================================================================
   fx.js — motion engine for the console and the dashboard.

   No framework, no build step. Everything the brief asks of Framer Motion /
   Motion One is here as compositor-only work: one requestAnimationFrame loop
   drives the canvas, the cursor and every smoothed value, and each element
   animates through transform/opacity only. Nothing reads layout during the
   loop, so there is no thrashing.

   Public surface (window.FX):
     FX.level(v)              'full' | 'calm' | 'off'
     FX.toast(opts)           slide-in notification with progress bar
     FX.countUp(el, n)        animated number
     FX.spark(svg, values)    sparkline path
     FX.burst(x, y, kind)     particle burst — 'buy' | 'sell'
     FX.enhance(root)         re-scan a subtree after it re-renders
   ========================================================================== */

(function () {
  "use strict";

  const root = document.documentElement;
  const reduced = matchMedia("(prefers-reduced-motion: reduce)");
  const finePointer = matchMedia("(hover: hover) and (pointer: fine)");

  /* ── Intensity ─────────────────────────────────────────────────────────── */

  const LEVELS = ["full", "calm", "off"];

  function currentLevel() {
    const saved = localStorage.getItem("fx");
    if (LEVELS.includes(saved)) return saved;
    return reduced.matches ? "calm" : "full";
  }

  function setLevel(v) {
    if (!LEVELS.includes(v)) return;
    localStorage.setItem("fx", v);
    root.dataset.fx = v;
    paintFxToggle();
    if (v === "full") startLoop(); else stopLoop();
  }

  root.dataset.fx = currentLevel();

  /* ── Shared pointer state, smoothed once per frame ─────────────────────── */

  const pointer = { x: innerWidth / 2, y: innerHeight / 2, sx: innerWidth / 2, sy: innerHeight / 2, active: false };

  addEventListener("pointermove", (e) => {
    pointer.x = e.clientX;
    pointer.y = e.clientY;
    if (!pointer.active) { pointer.sx = e.clientX; pointer.sy = e.clientY; }
    pointer.active = true;
  }, { passive: true });

  addEventListener("pointerleave", () => { pointer.active = false; }, { passive: true });

  // Exponential smoothing, framerate-independent: the fraction of the gap
  // closed per frame is derived from dt, so inertia feels identical at 60 and
  // 144 Hz instead of being twice as fast on the faster display.
  const smooth = (cur, target, dt, halfLife) =>
    target + (cur - target) * Math.pow(2, -dt / halfLife);

  /* ── Background: grid drift, particles, links, market line ─────────────── */

  let fxRoot, canvas, ctx, spot, grid;
  let particles = [];
  let dpr = 1, cw = 0, ch = 0;
  const series = [];           // background market polyline
  let seriesPhase = 0;

  function buildBackground() {
    fxRoot = document.createElement("div");
    fxRoot.id = "fx-root";
    fxRoot.setAttribute("aria-hidden", "true");
    fxRoot.innerHTML =
      '<div class="fx-grid"></div>' +
      '<div class="fx-aurora"><i></i><i></i><i></i></div>' +
      '<div class="fx-beams"><i></i><i></i><i></i></div>' +
      '<canvas class="fx-canvas"></canvas>' +
      '<div class="fx-spot"></div>' +
      '<div class="fx-vignette"></div>';
    document.body.insertBefore(fxRoot, document.body.firstChild);

    canvas = fxRoot.querySelector(".fx-canvas");
    ctx = canvas.getContext("2d", { alpha: true });
    spot = fxRoot.querySelector(".fx-spot");
    grid = fxRoot.querySelector(".fx-grid");
    resize();
  }

  function resize() {
    if (!canvas) return;
    dpr = Math.min(devicePixelRatio || 1, 2);   // cap at 2: 3x costs 2.25x the fill for no visible gain
    cw = innerWidth; ch = innerHeight;
    canvas.width = Math.round(cw * dpr);
    canvas.height = Math.round(ch * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    seedParticles();
    seedSeries();
  }

  function seedParticles() {
    const target = Math.round(Math.min(90, Math.max(26, (cw * ch) / 22000)));
    particles = Array.from({ length: target }, () => ({
      x: Math.random() * cw,
      y: Math.random() * ch,
      vx: (Math.random() - 0.5) * 0.12,
      vy: (Math.random() - 0.5) * 0.12,
      r: 0.7 + Math.random() * 1.5,
      a: 0.15 + Math.random() * 0.4,
    }));
  }

  function seedSeries() {
    series.length = 0;
    let v = 0.5;
    for (let i = 0; i < 160; i++) {
      v += (Math.random() - 0.5) * 0.06;
      v = Math.max(0.12, Math.min(0.88, v));
      series.push(v);
    }
  }

  function readToken(name) {
    return getComputedStyle(root).getPropertyValue(name).trim() || "#4f8cff";
  }

  let accentRGB = [79, 140, 255];
  function refreshTokens() {
    const hex = readToken("--accent");
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex);
    if (m) accentRGB = [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)];
  }

  function drawBackground(dt) {
    if (!ctx) return;
    ctx.clearRect(0, 0, cw, ch);
    const [r, g, b] = accentRGB;

    // Market line — drifts left, redrawn from a fixed sample buffer so it
    // never accumulates float error.
    seriesPhase += dt * 0.004;
    if (seriesPhase >= 1) {
      seriesPhase -= 1;
      series.push(Math.max(0.12, Math.min(0.88, series[series.length - 1] + (Math.random() - 0.5) * 0.06)));
      series.shift();
    }
    const step = cw / (series.length - 2);
    const baseY = ch * 0.62, amp = ch * 0.3;
    ctx.beginPath();
    for (let i = 0; i < series.length; i++) {
      const x = i * step - seriesPhase * step;
      const y = baseY - (series[i] - 0.5) * amp;
      i ? ctx.lineTo(x, y) : ctx.moveTo(x, y);
    }
    ctx.strokeStyle = `rgba(${r},${g},${b},0.14)`;
    ctx.lineWidth = 1.25;
    ctx.stroke();
    ctx.lineTo(cw, ch); ctx.lineTo(-step, ch); ctx.closePath();
    const fill = ctx.createLinearGradient(0, baseY - amp * 0.5, 0, ch);
    fill.addColorStop(0, `rgba(${r},${g},${b},0.07)`);
    fill.addColorStop(1, `rgba(${r},${g},${b},0)`);
    ctx.fillStyle = fill;
    ctx.fill();

    // Particles + their links. Single pass, O(n²/2) over ~60 points.
    const px = pointer.sx, py = pointer.sy;
    for (const p of particles) {
      p.x += p.vx * dt * 0.06;
      p.y += p.vy * dt * 0.06;

      if (pointer.active) {
        const dx = p.x - px, dy = p.y - py;
        const d2 = dx * dx + dy * dy;
        if (d2 < 26000 && d2 > 1) {
          const f = (1 - d2 / 26000) * 0.035;   // gentle push, restored by drift
          p.x += dx * f; p.y += dy * f;
        }
      }

      if (p.x < -20) p.x = cw + 20; else if (p.x > cw + 20) p.x = -20;
      if (p.y < -20) p.y = ch + 20; else if (p.y > ch + 20) p.y = -20;

      ctx.beginPath();
      ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(${r},${g},${b},${p.a})`;
      ctx.fill();
    }

    ctx.lineWidth = 0.6;
    for (let i = 0; i < particles.length; i++) {
      const a = particles[i];
      for (let j = i + 1; j < particles.length; j++) {
        const bp = particles[j];
        const dx = a.x - bp.x, dy = a.y - bp.y;
        const d2 = dx * dx + dy * dy;
        if (d2 > 15000) continue;
        ctx.beginPath();
        ctx.moveTo(a.x, a.y); ctx.lineTo(bp.x, bp.y);
        ctx.strokeStyle = `rgba(${r},${g},${b},${0.13 * (1 - d2 / 15000)})`;
        ctx.stroke();
      }
    }
  }

  /* ── Cursor glow: tracks the native pointer, never replaces it ─────────── */

  let cursorEl, ringEl, trail = [];
  const ring = { x: 0, y: 0, scale: 1 };

  function buildCursor() {
    cursorEl = document.createElement("div");
    cursorEl.className = "fx-cursor";
    cursorEl.setAttribute("aria-hidden", "true");
    cursorEl.innerHTML = '<div class="fx-cursor__glow"></div><div class="fx-cursor__ring"></div>';
    document.body.appendChild(cursorEl);
    ringEl = cursorEl.querySelector(".fx-cursor__ring");

    for (let i = 0; i < 7; i++) {
      const d = document.createElement("div");
      d.className = "fx-trail";
      d.setAttribute("aria-hidden", "true");
      document.body.appendChild(d);
      trail.push({ el: d, x: pointer.x, y: pointer.y });
    }
  }

  const INTERACTIVE = "a, button, .card, .widget, .nav-item, input, select, summary, [role='button']";

  function updateCursor(dt) {
    if (!cursorEl) return;
    if (pointer.active) cursorEl.classList.add("is-live");

    ring.x = smooth(ring.x, pointer.x, dt, 55);
    ring.y = smooth(ring.y, pointer.y, dt, 55);
    cursorEl.style.transform = `translate3d(${ring.x}px,${ring.y}px,0)`;
    ringEl.style.transform = `scale(${ring.scale})`;

    let px = pointer.x, py = pointer.y;
    for (let i = 0; i < trail.length; i++) {
      const t = trail[i];
      t.x = smooth(t.x, px, dt, 34 + i * 9);
      t.y = smooth(t.y, py, dt, 34 + i * 9);
      t.el.style.transform = `translate3d(${t.x}px,${t.y}px,0) scale(${1 - i / trail.length})`;
      t.el.style.opacity = String(0.30 * (1 - i / trail.length));
      px = t.x; py = t.y;
    }
  }

  addEventListener("pointerover", (e) => {
    if (e.target.closest && e.target.closest(INTERACTIVE)) ring.scale = 2.1;
  }, { passive: true });
  addEventListener("pointerout", (e) => {
    if (e.target.closest && e.target.closest(INTERACTIVE)) ring.scale = 1;
  }, { passive: true });

  /* ── Tilt, pointer-tracked sheen and border light ──────────────────────── */

  const TILTABLE = ".card, .widget, .candle-row, .strategy-row, .exchange-row, .status-card, .category-panel, .session";
  let tilted = null;

  function onTiltMove(el, e) {
    const r = el.getBoundingClientRect();
    const nx = (e.clientX - r.left) / r.width;
    const ny = (e.clientY - r.top) / r.height;
    el.style.setProperty("--ry", `${(nx - 0.5) * 5.4}deg`);
    el.style.setProperty("--rx", `${(0.5 - ny) * 4.2}deg`);
    el.style.setProperty("--ty", "-3px");
    el.style.setProperty("--sc", "1.012");
    el.style.setProperty("--gx", `${nx * 100}%`);
    el.style.setProperty("--gy", `${ny * 100}%`);
    el.style.setProperty("--edge", `${Math.atan2(ny - 0.5, nx - 0.5) * 57.2958 + 90}deg`);
  }

  function clearTilt(el) {
    el.classList.remove("is-tracking", "is-lit");
    el.style.removeProperty("--rx");
    el.style.removeProperty("--ry");
    el.style.removeProperty("--ty");
    el.style.removeProperty("--sc");
  }

  addEventListener("pointerover", (e) => {
    if (root.dataset.fx === "off" || !finePointer.matches) return;
    const el = e.target.closest && e.target.closest(TILTABLE);
    if (!el || el === tilted) return;
    if (tilted) clearTilt(tilted);
    tilted = el;
    el.classList.add("fx-tilt", "fx-glow", "fx-edge", "is-tracking", "is-lit");
  }, { passive: true });

  addEventListener("pointermove", (e) => {
    if (!tilted || root.dataset.fx === "off") return;
    if (!tilted.isConnected) { tilted = null; return; }
    onTiltMove(tilted, e);
  }, { passive: true });

  addEventListener("pointerout", (e) => {
    if (!tilted) return;
    const to = e.relatedTarget;
    if (to && tilted.contains(to)) return;
    clearTilt(tilted);
    tilted = null;
  }, { passive: true });

  /* ── Magnetic buttons + ripple ─────────────────────────────────────────── */

  const MAGNETIC = ".btn, .icon-btn, .ghost-btn, .nav-item, .bar__link";
  let magnet = null;

  addEventListener("pointerover", (e) => {
    if (root.dataset.fx === "off" || !finePointer.matches) return;
    const el = e.target.closest && e.target.closest(MAGNETIC);
    if (!el) return;
    if (magnet && magnet !== el) releaseMagnet(magnet);
    magnet = el;
    el.classList.add("fx-btn");
  }, { passive: true });

  addEventListener("pointermove", (e) => {
    if (!magnet || root.dataset.fx === "off") return;
    if (!magnet.isConnected) { magnet = null; return; }
    const r = magnet.getBoundingClientRect();
    const dx = e.clientX - (r.left + r.width / 2);
    const dy = e.clientY - (r.top + r.height / 2);
    // Pull capped at 6px: enough to feel alive, not enough to miss the target.
    magnet.style.setProperty("--mx", `${Math.max(-6, Math.min(6, dx * 0.28))}px`);
    magnet.style.setProperty("--my", `${Math.max(-6, Math.min(6, dy * 0.28))}px`);
  }, { passive: true });

  addEventListener("pointerout", (e) => {
    if (!magnet) return;
    const to = e.relatedTarget;
    if (to && magnet.contains(to)) return;
    releaseMagnet(magnet);
    magnet = null;
  }, { passive: true });

  function releaseMagnet(el) {
    el.style.removeProperty("--mx");
    el.style.removeProperty("--my");
  }

  addEventListener("pointerdown", (e) => {
    if (root.dataset.fx === "off") return;
    const el = e.target.closest && e.target.closest(MAGNETIC + ", .card, .widget");
    if (!el) return;
    const r = el.getBoundingClientRect();
    const size = Math.max(r.width, r.height) * 2;
    const ink = document.createElement("span");
    ink.className = "fx-ripple";
    ink.style.width = ink.style.height = `${size}px`;
    ink.style.left = `${e.clientX - r.left - size / 2}px`;
    ink.style.top = `${e.clientY - r.top - size / 2}px`;
    if (getComputedStyle(el).position === "static") el.style.position = "relative";
    el.appendChild(ink);
    ink.animate(
      [{ transform: "scale(0)", opacity: 0.28 }, { transform: "scale(1)", opacity: 0 }],
      { duration: 620, easing: "cubic-bezier(.22,.61,.36,1)" }
    ).onfinish = () => ink.remove();
  }, { passive: true });

  /* ── Value-change flash ────────────────────────────────────────────────── */

  // Only values where a change is meaningful. The freshness Age column is
  // deliberately absent: it ticks on every poll for every row, so flashing it
  // would strobe the whole table twice a minute and mean nothing.
  const FLASH = [
    ".candle-count b", ".category-count b", ".track__count b",
    ".card__stat b", ".summary b", ".widget__value", ".pill", ".card-status",
  ].join(",");

  const lastText = new WeakMap();

  function flashIfChanged(el) {
    const now = el.textContent.trim();
    const was = lastText.get(el);
    lastText.set(el, now);
    if (was === undefined || was === now || root.dataset.fx === "off") return;

    const a = parseFloat(was.replace(/[^\d.-]/g, ""));
    const b = parseFloat(now.replace(/[^\d.-]/g, ""));
    const up = Number.isFinite(a) && Number.isFinite(b) ? b > a : null;
    const cls = up === null ? "fx-up" : up ? "fx-up" : "fx-down";

    el.classList.remove("fx-up", "fx-down");
    void el.offsetWidth;                       // restart the animation
    el.classList.add(cls);
    setTimeout(() => el.classList.remove(cls), 1200);

    if (up !== null && Number.isFinite(a) && a !== b) {
      const tag = document.createElement("span");
      tag.className = `fx-delta ${up ? "up" : "down"}`;
      tag.textContent = up ? "▲" : "▼";
      el.after(tag);
      setTimeout(() => tag.remove(), 1300);
    }
  }

  function scanValues(scope) {
    (scope || document).querySelectorAll(FLASH).forEach(flashIfChanged);
  }

  const valueObserver = new MutationObserver((records) => {
    for (const rec of records) {
      const node = rec.type === "characterData" ? rec.target.parentElement : rec.target;
      if (!node || !node.closest) continue;
      const el = node.closest(FLASH);
      if (el) flashIfChanged(el);
      else if (rec.type === "childList") {
        rec.addedNodes.forEach((n) => { if (n.nodeType === 1) scanValues(n); });
      }
    }
  });

  /* ── Entrance staging ──────────────────────────────────────────────────── */

  const STAGGER = ".card, .widget, .candle-row, .strategy-row, .exchange-row, .track";

  function stage(scope) {
    const items = (scope || document).querySelectorAll(STAGGER);
    items.forEach((el, i) => {
      if (el.dataset.fxStaged) return;
      el.dataset.fxStaged = "1";
      el.style.setProperty("--i", String(Math.min(i, 14)));
      el.classList.add("fx-in");
    });
  }

  /* ── Sidebar indicator ─────────────────────────────────────────────────── */

  let pip = null;

  function positionPip() {
    const bar = document.querySelector(".sidebar");
    if (!bar) return;
    if (!pip) {
      pip = document.createElement("div");
      pip.className = "fx-nav-pip";
      pip.setAttribute("aria-hidden", "true");
      bar.style.position = "relative";
      bar.appendChild(pip);
    }
    const active = bar.querySelector(".nav-item.active");
    if (!active) { pip.style.opacity = "0"; return; }
    pip.style.opacity = "1";
    pip.style.height = `${active.offsetHeight - 12}px`;
    pip.style.transform = `translate3d(0, ${active.offsetTop + 6}px, 0)`;
  }

  /* ── Numbers and sparklines ────────────────────────────────────────────── */

  function countUp(el, value, opts) {
    const o = opts || {};
    const from = Number(el.dataset.fxVal || 0);
    const to = Number(value) || 0;
    el.dataset.fxVal = String(to);
    if (root.dataset.fx === "off" || reduced.matches || from === to) {
      el.textContent = format(to, o);
      return;
    }
    const dur = o.duration || 620;
    const t0 = performance.now();
    const tick = (t) => {
      const k = Math.min(1, (t - t0) / dur);
      const e = 1 - Math.pow(1 - k, 3);
      el.textContent = format(from + (to - from) * e, o);
      if (k < 1) requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }

  function format(n, o) {
    const d = o.decimals || 0;
    return (o.prefix || "") + n.toFixed(d) + (o.suffix || "");
  }

  function spark(svg, values) {
    if (!svg || !values || values.length < 2) return;
    const w = 100, h = 26;
    const min = Math.min(...values), max = Math.max(...values);
    const span = max - min || 1;
    const pts = values.map((v, i) => [
      (i / (values.length - 1)) * w,
      h - ((v - min) / span) * (h - 4) - 2,
    ]);
    const d = pts.map((p, i) => `${i ? "L" : "M"}${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join(" ");
    svg.setAttribute("viewBox", `0 0 ${w} ${h}`);
    svg.setAttribute("preserveAspectRatio", "none");
    svg.innerHTML =
      `<path class="area" d="${d} L${w} ${h} L0 ${h} Z"></path><path d="${d}"></path>`;
  }

  /* ── Toasts ────────────────────────────────────────────────────────────── */

  let toastHost;

  function toast(opts) {
    const o = typeof opts === "string" ? { body: opts } : (opts || {});
    if (!toastHost) {
      toastHost = document.createElement("div");
      toastHost.id = "fx-toasts";
      toastHost.setAttribute("role", "status");
      toastHost.setAttribute("aria-live", "polite");
      document.body.appendChild(toastHost);
    }
    const life = o.duration || 4200;
    const el = document.createElement("div");
    el.className = `fx-toast ${o.kind || ""}`;
    el.innerHTML =
      (o.title ? `<div class="fx-toast__title">${esc(o.title)}</div>` : "") +
      (o.body ? `<div class="fx-toast__body">${esc(o.body)}</div>` : "") +
      '<div class="fx-toast__bar"></div>';
    toastHost.appendChild(el);

    const bar = el.querySelector(".fx-toast__bar");
    bar.animate([{ transform: "scaleX(1)" }, { transform: "scaleX(0)" }],
      { duration: life, easing: "linear", fill: "forwards" });

    const close = () => {
      el.classList.add("is-out");
      setTimeout(() => el.remove(), 320);
    };
    const timer = setTimeout(close, life);
    el.addEventListener("click", () => { clearTimeout(timer); close(); });
    return close;
  }

  function esc(s) {
    return String(s ?? "").replace(/[&<>"']/g, (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }

  /* ── Order burst ───────────────────────────────────────────────────────────
     Built and callable, but nothing binds it yet: this system has no execution
     engine, so there is no real BUY/SELL event to hang it on. Wire it at the
     point an order actually fills. */

  function burst(x, y, kind) {
    if (root.dataset.fx === "off" || reduced.matches) return;
    const colour = kind === "sell" ? readToken("--red") : readToken("--green");
    for (let i = 0; i < 18; i++) {
      const dot = document.createElement("div");
      dot.className = "fx-burst";
      dot.style.background = colour;
      dot.style.left = `${x}px`;
      dot.style.top = `${y}px`;
      document.body.appendChild(dot);
      const ang = (Math.PI * 2 * i) / 18 + Math.random() * 0.3;
      const dist = 42 + Math.random() * 58;
      dot.animate([
        { transform: "translate3d(0,0,0) scale(1)", opacity: 1 },
        { transform: `translate3d(${Math.cos(ang) * dist}px, ${Math.sin(ang) * dist}px, 0) scale(0)`, opacity: 0 },
      ], { duration: 620 + Math.random() * 260, easing: "cubic-bezier(.2,.7,.3,1)" })
        .onfinish = () => dot.remove();
    }
  }

  /* ── Effects toggle in the header ──────────────────────────────────────── */

  const FX_ICONS = {
    full: '<path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6l1.4 1.4M17 17l1.4 1.4M5.6 18.4L7 17M17 7l1.4-1.4"/><circle cx="12" cy="12" r="3.4"/>',
    calm: '<circle cx="12" cy="12" r="3.4"/><path d="M12 4.5v1.6M12 17.9v1.6M4.5 12h1.6M17.9 12h1.6"/>',
    off:  '<circle cx="12" cy="12" r="3.4"/><path d="M4 20L20 4"/>',
  };

  function paintFxToggle() {
    const btn = document.getElementById("fx-toggle");
    if (!btn) return;
    const lv = root.dataset.fx;
    btn.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">${FX_ICONS[lv]}</svg>`;
    btn.title = `Motion: ${lv} — click to cycle`;
    btn.setAttribute("aria-label", `Motion effects: ${lv}. Click to cycle.`);
  }

  function mountFxToggle() {
    const host = document.querySelector(".header-right, .bar__right");
    if (!host || document.getElementById("fx-toggle")) return;
    const btn = document.createElement("button");
    btn.id = "fx-toggle";
    btn.className = host.classList.contains("bar__right") ? "ghost-btn" : "icon-btn";
    btn.addEventListener("click", () => {
      setLevel(LEVELS[(LEVELS.indexOf(root.dataset.fx) + 1) % LEVELS.length]);
      toast({ title: "Motion", body: `Effects set to ${root.dataset.fx}.`, duration: 2200 });
    });
    const themeBtn = document.getElementById("theme-toggle");
    host.insertBefore(btn, themeBtn || null);
    paintFxToggle();
  }

  /* ── The one loop ──────────────────────────────────────────────────────── */

  let raf = 0, last = 0;

  function frame(t) {
    const dt = Math.min(48, t - last || 16);
    last = t;

    pointer.sx = smooth(pointer.sx, pointer.x, dt, 90);
    pointer.sy = smooth(pointer.sy, pointer.y, dt, 90);

    if (spot) {
      spot.style.setProperty("--fx-x", `${(pointer.sx / cw) * 100}%`);
      spot.style.setProperty("--fx-y", `${(pointer.sy / ch) * 100}%`);
    }
    if (grid) {
      // Parallax: the grid lags the pointer by a few pixels, which reads as depth
      grid.style.transform =
        `translate3d(${(pointer.sx / cw - 0.5) * -14}px, ${(pointer.sy / ch - 0.5) * -14}px, 0)`;
    }

    drawBackground(dt);
    updateCursor(dt);

    raf = requestAnimationFrame(frame);
  }

  function startLoop() {
    if (raf || root.dataset.fx !== "full" || document.hidden) return;
    last = performance.now();
    raf = requestAnimationFrame(frame);
  }

  function stopLoop() {
    if (!raf) return;
    cancelAnimationFrame(raf);
    raf = 0;
    if (ctx) ctx.clearRect(0, 0, cw, ch);
  }

  document.addEventListener("visibilitychange", () => {
    document.hidden ? stopLoop() : startLoop();
  });

  addEventListener("resize", () => { resize(); positionPip(); }, { passive: true });

  /* ── Boot ──────────────────────────────────────────────────────────────── */

  function enhance(scope) {
    stage(scope);
    scanValues(scope);
    positionPip();
  }

  function boot() {
    refreshTokens();
    buildBackground();
    if (finePointer.matches) buildCursor();
    mountFxToggle();

    const main = document.querySelector("main") || document.body;
    valueObserver.observe(main, { subtree: true, childList: true, characterData: true });

    scanValues();
    stage();
    positionPip();
    startLoop();

    // The theme toggle rewrites the palette; the canvas caches it, so re-read.
    const themeBtn = document.getElementById("theme-toggle");
    if (themeBtn) themeBtn.addEventListener("click", () => setTimeout(refreshTokens, 60));

    addEventListener("hashchange", () => {
      setTimeout(() => { positionPip(); stage(); }, 30);
    });

    // Nav clicks reposition the indicator before the hash handler runs, so the
    // pip moves with the press rather than after it.
    document.addEventListener("click", (e) => {
      if (e.target.closest && e.target.closest(".nav-item")) setTimeout(positionPip, 20);
    });
  }

  if (document.readyState === "loading") addEventListener("DOMContentLoaded", boot);
  else boot();

  window.FX = { level: setLevel, toast, countUp, spark, burst, enhance };
})();
