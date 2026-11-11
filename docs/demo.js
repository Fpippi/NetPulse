// Static demo of the NetPulse dashboard for GitHub Pages.
// The real app is Blazor Server and measures the network it runs on; here the samples are
// simulated in the browser (at 5x real time, so the page visibly moves), but stats and chart follow the same rules as NetPulse.Core
// (LatencyStats and ChartDownsampler).
(() => {
  "use strict";

  const INTERVAL_MS = 5000;           // MonitorOptions.IntervalSeconds default
  const HISTORY_MS = 24 * 3600 * 1000;
  const WIDTH = 600, HEIGHT = 120, MAX_POINTS = 300;
  const WINDOWS = [
    { label: "15m", ms: 15 * 60 * 1000 },
    { label: "1h", ms: 3600 * 1000 },
    { label: "24h", ms: 24 * 3600 * 1000 },
  ];

  // Small deterministic PRNG so every visitor sees the same story.
  function mulberry32(seed) {
    return () => {
      seed |= 0; seed = (seed + 0x6d2b79f5) | 0;
      let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  // Evening congestion: 0 at night, peaks around 21:00 local time.
  function load(ts) {
    const h = new Date(ts).getHours() + new Date(ts).getMinutes() / 60;
    return Math.max(0, Math.cos(((h - 21) / 24) * 2 * Math.PI)) ** 3;
  }

  const PROFILES = [
    // Wired/Wi-Fi link to the router: tiny latency, rare hiccups.
    { target: "gateway", base: 1.8, noise: 1.2, busy: 1.5, spike: 0.0004, spikeMs: 20, loss: 0 },
    { target: "1.1.1.1", base: 11, noise: 2.5, busy: 8, spike: 0.0008, spikeMs: 50, loss: 0 },
    // A route that suffers in the evening: jitter and loss.
    { target: "8.8.8.8", base: 18, noise: 4, busy: 35, spike: 0.003, spikeMs: 120, loss: 0.0015 },
  ];

  function makeSample(p, rnd, ts) {
    const l = load(ts);
    if (rnd() < p.loss * (1 + 4 * l)) return { ts, ok: false, ms: null };
    let ms = p.base + p.busy * l * rnd() + p.noise * rnd();
    if (rnd() < p.spike * (1 + 3 * l)) ms += p.spikeMs * (0.4 + rnd());
    return { ts, ok: true, ms: Math.round(ms) }; // Ping.RoundtripTime has 1 ms resolution
  }

  const now0 = Math.floor(Date.now() / INTERVAL_MS) * INTERVAL_MS;
  const series = PROFILES.map((p, i) => {
    const rnd = mulberry32(1234 + i * 977);
    const samples = [];
    for (let ts = now0 - HISTORY_MS; ts <= now0; ts += INTERVAL_MS) samples.push(makeSample(p, rnd, ts));
    return { p, rnd, samples };
  });

  // Same algorithm as LatencyStats.From.
  function stats(samples) {
    if (samples.length === 0) return { count: 0, loss: 0 };
    const lat = samples.filter(s => s.ok).map(s => s.ms);
    const loss = 100 * (samples.length - lat.length) / samples.length;
    if (lat.length === 0) return { count: samples.length, loss };
    let jitter = null;
    if (lat.length > 1) {
      let sum = 0;
      for (let i = 1; i < lat.length; i++) sum += Math.abs(lat[i] - lat[i - 1]);
      jitter = sum / (lat.length - 1);
    }
    let min = Infinity, max = -Infinity, total = 0;
    for (const v of lat) { total += v; if (v < min) min = v; if (v > max) max = v; }
    return { count: samples.length, loss, avg: total / lat.length, min, max, jitter };
  }

  function quality(s) {
    if (s.count === 0) return "Unknown";
    const avg = s.avg ?? Number.MAX_VALUE, jitter = s.jitter ?? 0;
    if (s.loss >= 5 || jitter >= 30 || avg >= 150) return "Poor";
    if (s.loss > 0 || jitter >= 10 || avg >= 80) return "Fair";
    return "Good";
  }

  // Same algorithm as ChartDownsampler.Downsample: max per bucket keeps spikes visible.
  function downsample(samples, maxPoints) {
    const buckets = Math.min(maxPoints, samples.length), out = [];
    for (let b = 0; b < buckets; b++) {
      const start = Math.floor(b * samples.length / buckets);
      const end = Math.floor((b + 1) * samples.length / buckets);
      let max = null, hasLoss = false;
      for (let i = start; i < end; i++) {
        const s = samples[i];
        if (!s.ok) hasLoss = true;
        else if (max === null || s.ms > max) max = s.ms;
      }
      out.push({ index: b, max, hasLoss });
    }
    return out;
  }

  // ---- Rendering --------------------------------------------------------------------------
  // Cards are built once and then updated in place, so values can animate and the chart can
  // scroll smoothly instead of being redrawn from scratch on every round.

  const TICK_MS = 1000;               // one simulated 5 s round per second: 5x real time
  const SVG_NS = "http://www.w3.org/2000/svg";
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  const fmtMs = v => (v == null ? "–" : v.toFixed(1) + " ms");
  const fmtPct = v => (Math.round(v * 10) / 10).toString() + " %";
  const f = v => (Math.round(v * 100) / 100).toString();

  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  function svg(tag, attrs) {
    const e = document.createElementNS(SVG_NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    return e;
  }

  // Tweens a number shown in an element, so stats glide to their new value.
  function tween(node, to, fmt) {
    const from = node._value;
    node._value = to;
    if (from == null || to == null || reducedMotion || from === to) {
      node.textContent = fmt(to);
      return;
    }
    const start = performance.now(), dur = 600;
    cancelAnimationFrame(node._raf);
    const step = now => {
      const t = Math.min(1, (now - start) / dur), e = 1 - (1 - t) ** 3;
      node.textContent = fmt(from + (to - from) * e);
      if (t < 1) node._raf = requestAnimationFrame(step);
    };
    node._raf = requestAnimationFrame(step);
  }

  function buildCard(target) {
    const card = el("section", "card");
    const head = el("div", "card-head");
    head.append(el("h2", null, target));
    const right = el("div", "card-right");
    const now = el("span", "now");
    const badge = el("span", "badge");
    right.append(now, badge);
    head.append(right);

    const dl = el("dl", "stats"), dd = {};
    for (const [key, label] of [["avg", "Avg"], ["min", "Min"], ["max", "Max"], ["jitter", "Jitter"], ["loss", "Loss"], ["count", "Samples"]]) {
      const div = el("div");
      div.append(el("dt", null, label), dd[key] = el("dd"));
      dl.append(div);
    }

    const wrap = el("div", "chart-wrap");
    const scaleLabel = el("span", "chart-max");
    const chart = svg("svg", { class: "chart", viewBox: `0 0 ${WIDTH} ${HEIGHT}`, preserveAspectRatio: "none", role: "img", "aria-label": "Latency over time" });
    const scroll = svg("g", { class: "scroll" });
    const losses = svg("g", {});
    const line = svg("polyline", { class: "line" });
    scroll.append(losses, line);
    chart.append(scroll);
    const dot = el("span", "live-dot");
    wrap.append(scaleLabel, chart, dot);

    card.append(head, dl, wrap);
    return { card, now, badge, dd, scaleLabel, scroll, losses, line, dot };
  }

  function updateCard(c, samples, animateScroll) {
    const s = stats(samples), q = quality(s);
    c.badge.className = "badge " + q.toLowerCase();
    c.badge.textContent = q;

    const last = samples[samples.length - 1];
    c.now.textContent = last.ok ? `${last.ms} ms` : "timeout";
    c.now.classList.toggle("lost", !last.ok);
    c.now.classList.remove("flash");
    void c.now.offsetWidth;            // restart the flash animation
    c.now.classList.add("flash");

    tween(c.dd.avg, s.avg ?? null, fmtMs);
    tween(c.dd.min, s.min ?? null, fmtMs);
    tween(c.dd.max, s.max ?? null, fmtMs);
    tween(c.dd.jitter, s.jitter ?? null, fmtMs);
    tween(c.dd.loss, s.loss, fmtPct);
    c.dd.count.textContent = s.count;

    // Chart: same downsampling and scale as LatencyChart.razor.
    const pts = downsample(samples, MAX_POINTS);
    const peak = Math.max(0, ...pts.filter(p => p.max !== null).map(p => p.max));
    const scale = Math.max(10, peak * 1.1);
    const dx = WIDTH / (pts.length - 1);
    const y = v => HEIGHT - v / scale * HEIGHT;
    c.scaleLabel.textContent = `${Math.round(scale)} ms`;
    c.line.setAttribute("points", pts.filter(p => p.max !== null).map(p => `${f(p.index * dx)},${f(y(p.max))}`).join(" "));
    c.losses.replaceChildren(...pts.filter(p => p.hasLoss)
      .map(p => svg("line", { class: "loss", x1: f(p.index * dx), x2: f(p.index * dx), y1: 0, y2: HEIGHT })));

    const lastPt = pts[pts.length - 1];
    c.dot.style.top = `${(lastPt.max === null ? HEIGHT : y(lastPt.max)) / HEIGHT * 100}%`;
    c.dot.classList.toggle("lost", !last.ok);

    // When every sample is its own point (15m window), a new round shifts the series left by
    // exactly one step: start one step to the right and slide into place for a seamless scroll.
    c.scroll.style.transition = "none";
    c.scroll.style.transform = animateScroll && !reducedMotion ? `translateX(${dx}px)` : "";
    if (animateScroll && !reducedMotion) {
      void c.scroll.getBoundingClientRect();
      c.scroll.style.transition = `transform ${TICK_MS}ms linear`;
      c.scroll.style.transform = "translateX(0)";
    }
  }

  let windowMs = WINDOWS[0].ms;
  const main = document.getElementById("cards");
  const nav = document.getElementById("windows");
  const ordered = [...series].sort((a, b) => a.p.target.localeCompare(b.p.target));
  main.replaceChildren();
  for (const s of ordered) {
    s.view = buildCard(s.p.target);
    main.append(s.view.card);
  }

  function render(animateScroll) {
    const latest = series[0].samples[series[0].samples.length - 1].ts;
    const since = latest - windowMs;
    for (const s of ordered) {
      const win = s.samples.filter(x => x.ts > since);
      updateCard(s.view, win, animateScroll && win.length <= MAX_POINTS);
    }
  }

  nav.innerHTML = WINDOWS.map((w, i) => `<button data-i="${i}"${i === 0 ? ' class="active"' : ""}>${w.label}</button>`).join("");
  nav.addEventListener("click", e => {
    const btn = e.target.closest("button");
    if (!btn) return;
    windowMs = WINDOWS[+btn.dataset.i].ms;
    nav.querySelectorAll("button").forEach(b => b.classList.toggle("active", b === btn));
    render(false);
  });

  // A new ping round every tick, like PingMonitorService (sped up 5x so the demo feels alive).
  setInterval(() => {
    for (const s of series) {
      const ts = s.samples[s.samples.length - 1].ts + INTERVAL_MS;
      s.samples.push(makeSample(s.p, s.rnd, ts));
      s.samples.shift();
    }
    render(true);
  }, TICK_MS);

  render(false);
})();
