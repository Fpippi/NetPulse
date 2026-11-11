// Static demo of the NetPulse dashboard for GitHub Pages.
// The real app is Blazor Server and measures the network it runs on; here the samples are
// simulated in the browser, but stats and chart follow the same rules as NetPulse.Core
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

  const ms = v => (v == null ? "–" : v.toFixed(1) + " ms");
  const f = v => (Math.round(v * 100) / 100).toString();
  const esc = s => s.replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);

  function chart(samples) {
    const pts = downsample(samples, MAX_POINTS);
    if (pts.length < 2) return '<div class="chart-empty">Not enough data yet</div>';
    const peak = Math.max(0, ...pts.filter(p => p.max !== null).map(p => p.max));
    const scale = Math.max(10, peak * 1.1);
    const x = i => i * WIDTH / (pts.length - 1);
    const line = pts.filter(p => p.max !== null).map(p => `${f(x(p.index))},${f(HEIGHT - p.max / scale * HEIGHT)}`).join(" ");
    const losses = pts.filter(p => p.hasLoss)
      .map(p => `<line class="loss" x1="${f(x(p.index))}" x2="${f(x(p.index))}" y1="0" y2="${HEIGHT}" />`).join("");
    return `<div class="chart-wrap"><span class="chart-max">${Math.round(scale)} ms</span>` +
      `<svg class="chart" viewBox="0 0 ${WIDTH} ${HEIGHT}" preserveAspectRatio="none" role="img" aria-label="Latency over time">` +
      `${losses}<polyline class="line" points="${line}" /></svg></div>`;
  }

  let windowMs = WINDOWS[0].ms;
  const main = document.getElementById("cards");
  const nav = document.getElementById("windows");

  function render() {
    const latest = series[0].samples[series[0].samples.length - 1].ts;
    const since = latest - windowMs;
    main.innerHTML = [...series].sort((a, b) => a.p.target.localeCompare(b.p.target)).map(({ p, samples }) => {
      const win = samples.filter(s => s.ts > since);
      const s = stats(win), q = quality(s);
      return `<section class="card"><div class="card-head"><h2>${esc(p.target)}</h2>` +
        `<span class="badge ${q.toLowerCase()}">${q}</span></div><dl class="stats">` +
        `<div><dt>Avg</dt><dd>${ms(s.avg)}</dd></div><div><dt>Min</dt><dd>${ms(s.min)}</dd></div>` +
        `<div><dt>Max</dt><dd>${ms(s.max)}</dd></div><div><dt>Jitter</dt><dd>${ms(s.jitter)}</dd></div>` +
        `<div><dt>Loss</dt><dd>${(Math.round(s.loss * 10) / 10).toString()} %</dd></div>` +
        `<div><dt>Samples</dt><dd>${s.count}</dd></div></dl>${chart(win)}</section>`;
    }).join("");
  }

  nav.innerHTML = WINDOWS.map((w, i) => `<button data-i="${i}"${i === 0 ? ' class="active"' : ""}>${w.label}</button>`).join("");
  nav.addEventListener("click", e => {
    const btn = e.target.closest("button");
    if (!btn) return;
    windowMs = WINDOWS[+btn.dataset.i].ms;
    nav.querySelectorAll("button").forEach(b => b.classList.toggle("active", b === btn));
    render();
  });

  // "Live" updates: one new round every 5 seconds, like PingMonitorService.
  setInterval(() => {
    for (const s of series) {
      const ts = s.samples[s.samples.length - 1].ts + INTERVAL_MS;
      s.samples.push(makeSample(s.p, s.rnd, ts));
      s.samples.shift();
    }
    render();
  }, INTERVAL_MS);

  render();
})();
