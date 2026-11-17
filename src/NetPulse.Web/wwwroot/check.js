// NetPulse connection check, run in the browser of the device being tested (a phone on the Wi-Fi).
// It measures the device's link to NetPulse, then whether the device reaches the router and the
// Internet, and posts the numbers to /api/check/result, which returns the verdict. No libraries.
(() => {
    "use strict";

    const root = document.getElementById("client-check");
    if (!root) {
        return;
    }

    const ROUND_TRIPS = 30;           // requests to NetPulse; the first one warms the connection up
    const ROUND_TRIP_GAP_MS = 200;
    const LOST_AFTER_MS = 2000;       // a request without an answer by then counts as lost
    const TRANSFER_MS = 4000;         // each throughput direction
    const UPLOAD_CHUNK = 2 * 1024 * 1024;
    const DOWNLOAD_BYTES = 16 * 1024 * 1024;
    const PROBE_TIMEOUT_MS = 4000;    // router and Internet

    const start = root.querySelector("[data-action=start]");
    const verdict = root.querySelector("[data-verdict]");
    const errorBox = root.querySelector("[data-error]");
    const step = name => root.querySelector(`[data-step=${name}] output`);
    const api = path => new URL(`api/check/${path}`, document.baseURI).toString();

    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    const fixed = (value, digits = 0) => value.toFixed(digits);

    class SignedOut extends Error {}

    async function fetchWithTimeout(url, options, timeoutMs) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeoutMs);
        try {
            return await fetch(url, { cache: "no-store", ...options, signal: controller.signal });
        } finally {
            clearTimeout(timer);
        }
    }

    function checkSignedIn(response) {
        if (response.status === 401 || response.redirected) {
            throw new SignedOut();
        }
    }

    async function measureRoundTrips() {
        const out = step("latency");
        const results = [];
        for (let i = 0; i <= ROUND_TRIPS; i++) {
            const started = performance.now();
            let rtt = null;
            try {
                const response = await fetchWithTimeout(api(`ping?n=${i}`), {}, LOST_AFTER_MS);
                checkSignedIn(response);
                rtt = performance.now() - started;
            } catch (e) {
                if (e instanceof SignedOut) {
                    throw e;
                }
            }

            if (i > 0) {
                results.push(rtt);
                const ok = results.filter(r => r !== null);
                const lost = results.length - ok.length;
                out.textContent = `${results.length}/${ROUND_TRIPS} · ` +
                    (ok.length ? `${fixed(median(ok))} ms` : "no reply") + (lost ? ` · ${lost} lost` : "");
            }

            await sleep(ROUND_TRIP_GAP_MS);
        }

        return results;
    }

    function median(values) {
        const sorted = [...values].sort((a, b) => a - b);
        const mid = Math.floor(sorted.length / 2);
        return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    // Reads downloads for TRANSFER_MS and counts the bytes; the clock starts at the first byte.
    async function measureDownload() {
        const out = step("download");
        const deadline = performance.now() + TRANSFER_MS + LOST_AFTER_MS;
        let bytes = 0;
        let first = null;
        while (performance.now() < deadline && (first === null || performance.now() - first < TRANSFER_MS)) {
            const response = await fetchWithTimeout(api(`download?bytes=${DOWNLOAD_BYTES}`), {}, TRANSFER_MS + LOST_AFTER_MS);
            checkSignedIn(response);
            const reader = response.body.getReader();
            for (;;) {
                const { done, value } = await reader.read();
                if (done) {
                    break;
                }

                first ??= performance.now();
                bytes += value.length;
                const elapsed = performance.now() - first;
                if (elapsed > 250) {
                    out.textContent = `${fixed(mbps(bytes, elapsed))} Mbps`;
                }

                if (elapsed >= TRANSFER_MS) {
                    await reader.cancel();
                    break;
                }
            }
        }

        return first === null ? null : mbps(bytes, performance.now() - first);
    }

    // Posts random chunks for TRANSFER_MS.
    async function measureUpload() {
        const out = step("upload");
        const chunk = new Uint8Array(UPLOAD_CHUNK);
        for (let i = 0; i < chunk.length; i += 65536) {
            crypto.getRandomValues(chunk.subarray(i, i + 65536));
        }

        const started = performance.now();
        let bytes = 0;
        while (performance.now() - started < TRANSFER_MS) {
            const response = await fetchWithTimeout(api("upload"), { method: "POST", body: chunk }, TRANSFER_MS * 2);
            checkSignedIn(response);
            bytes += chunk.length;
            out.textContent = `${fixed(mbps(bytes, performance.now() - started))} Mbps`;
        }

        return mbps(bytes, performance.now() - started);
    }

    const mbps = (bytes, ms) => bytes * 8 / (ms / 1000) / 1e6;

    // The browser cannot ping, so it loads the address "blind" (no-cors): any answer means reachable.
    // An error that comes back fast (connection refused, no web page) says nothing either way.
    async function probe(name, url) {
        const out = step(name);
        if (!url) {
            return "Unknown";
        }

        out.textContent = "…";
        const started = performance.now();
        try {
            await fetchWithTimeout(url, { mode: "no-cors" }, PROBE_TIMEOUT_MS);
            out.textContent = `reachable · ${fixed(performance.now() - started)} ms`;
            return "Reachable";
        } catch {
            if (performance.now() - started >= PROBE_TIMEOUT_MS - 50) {
                out.textContent = "no answer";
                return "Unreachable";
            }

            out.textContent = name === "router" ? "answers, no web page" : "blocked";
            return name === "router" ? "Reachable" : "Unknown";
        }
    }

    function show(report) {
        const quality = verdict.querySelector("[data-quality]");
        quality.className = `badge ${report.severity.toLowerCase()}`;
        quality.textContent = report.label;
        verdict.querySelector("[data-headline]").textContent = report.headline;
        const advice = verdict.querySelector("[data-advice]");
        advice.replaceChildren(...report.advice.map(text => {
            const li = document.createElement("li");
            li.textContent = text;
            return li;
        }));
        verdict.hidden = false;
    }

    function fail(message) {
        errorBox.textContent = message;
        errorBox.hidden = false;
    }

    async function run() {
        start.disabled = true;
        start.textContent = "Checking…";
        verdict.hidden = true;
        errorBox.hidden = true;
        root.querySelectorAll(".check-steps output").forEach(o => {
            if (o.textContent !== "skipped") {
                o.textContent = "–";
            }
        });

        const measurement = { roundTripsMs: [], downloadMbps: null, uploadMbps: null, router: "Unknown", internet: "Unknown" };
        try {
            measurement.roundTripsMs = await measureRoundTrips();
            if (measurement.roundTripsMs.some(r => r !== null)) {
                try {
                    measurement.downloadMbps = await measureDownload();
                    measurement.uploadMbps = await measureUpload();
                } catch (e) {
                    if (e instanceof SignedOut) {
                        throw e;
                    }
                }
            }

            const router = root.dataset.router;
            measurement.router = await probe("router", router ? `http://${router}/` : null);
            measurement.internet = await probe("internet", root.dataset.internet || null);
        } catch (e) {
            if (e instanceof SignedOut) {
                fail("Your session has expired: reload the page and sign in again.");
                reset();
                return;
            }
        }

        let report = null;
        try {
            const response = await fetchWithTimeout(api("result"), {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(measurement),
            }, 10000);
            checkSignedIn(response);
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            report = await response.json();
        } catch (e) {
            fail(e instanceof SignedOut
                ? "Your session has expired: reload the page and sign in again."
                : "NetPulse did not answer with the result: this device lost the connection to it. " +
                  "That points to the Wi-Fi or the router.");
        }

        if (report) {
            show(report);
        }

        reset();
    }

    function reset() {
        start.disabled = false;
        start.textContent = "Run the check again";
    }

    start.addEventListener("click", run);
})();
