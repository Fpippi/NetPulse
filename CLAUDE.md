# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

NetPulse is a free, open-source, self-hosted network quality monitor written in C# / .NET 10.
It is a **portfolio project**: code quality, clear architecture, tests and documentation matter as
much as features. Budget is zero: use only free, open-source dependencies and free services.

## First task (important)

The initial scaffold was written **without a .NET SDK available**, so it has never been compiled.
Before anything else:

1. `dotnet restore && dotnet build` — fix any compile errors or package version issues
   (versions in the `.csproj` files may need bumping to the latest stable).
2. `dotnet test` — make sure all tests pass.
3. `dotnet run --project src/NetPulse.Web` — check the dashboard at http://localhost:5080
   shows data after a few seconds.

## Commands

```bash
dotnet build                               # build everything
dotnet test                                # run NUnit tests
dotnet run --project src/NetPulse.Web      # run the app (http://localhost:5080)
docker build -t netpulse .                 # container image
```

## Architecture

- `src/NetPulse.Core` — domain logic, **no external dependencies**.
  - `Models/PingSample` — one measurement.
  - `Probing/` — `IPingProbe` + `IcmpPingProbe`, `GatewayResolver` (IPv4/IPv6 router, `HasGlobalIpv6Address`);
    `ProbeTarget` parses a target into ping (`ping4:`/`ping6:` pin a host name to an `IpFamily`, resolved by
    `IHostResolver`; `gateway`/`gateway6`; `FamilyOf(name)`) / `dns:name[@server]` / `http(s)://` (`ProbeKind`); `DnsProbe` (raw UDP A query via `DnsWire`),
    `HttpProbe` (GET until headers), `TargetProbe` dispatches by kind with `ProbeTimeouts`.
    `Stats/QualityThresholds` gives each kind its own Good/Fair/Poor scale (`LatencyStats.Rate`).
  - `Stats/` — `LatencyStats` (avg/min/max/jitter/loss/quality), `ChartDownsampler`.
  - `Storage/I*Store` — persistence abstractions (samples, targets, outages, speed tests, LAN devices);
    `RetentionPolicy` (days per `DataKind`, 0 = forever), `CompactionPolicy` (VACUUM after big deletes), `DataSize`.
  - `Outages/` — `OutageTracker` (N consecutive lost pings → `Outage`, ends at the next reply),
    `OutageSummary` (downtime/availability over a window), `DurationFormat`.
  - `SpeedTest/` — `HttpSpeedTest` (latency/download/upload over `HttpClient`), `SpeedTestPlan`
    (validation), `TransferMeter` (Mbps with TCP warm-up skipped), `SpeedTestSchedule`, `SpeedTestSummary`;
    `LatencySampler` pings idle and while a transfer runs, `Bufferbloat` (medians + A+…F grade).
  - `Reports/` — `IspReportBuilder` (→ `IspReport`), `ReportPeriod` (days or from/to dates), `Csv` +
    `CsvExport` (RFC 4180, invariant numbers, UTC ISO 8601), `JsonExport` (streamed arrays, same fields as CSV). `Outages/OutageClassifier` labels outages
    LocalNetwork / Internet / SingleHost / Unknown; `Stats/LatencyAccumulator` streams `LatencyStats`.
  - `Notifications/` — `Notification` (+ `INotificationPublisher`, `INotificationChannel`), `NotificationFactory`
    (outage/device/speed-test messages), `NotificationBatch` (merges bursts), `DeliveryQueue` (per-channel retry
    with backoff and expiry), `NtfyChannel` / `TelegramChannel` / `WebhookChannel` (static `BuildRequest` for tests).
  - `Traceroute/` — `Tracer` (TTL 1…N until reached / N silent hops; `IsTraceable`), `ITraceProbe` +
    `IcmpTraceProbe` (`PingOptions.Ttl`), `TraceResult.Summary`, `OutageTrace`, `TraceLookup` (trace of an outage).
  - `Metrics/` — `PrometheusWriter` (hand-written text format, invariant numbers, label escaping),
    `NetPulseMetrics` (renders a `MetricsSnapshot`), `MonitorHeartbeat` (last monitoring cycle, `IsAlive`).
  - `Targets/` — `TargetName` (validation/normalization), `TargetManager` (configured + custom targets),
    `Ipv6Defaults` (adds `gateway6` + public IPv6 hosts per `Ipv6Mode`). `OutageClassifier` compares IPv6
    targets only with IPv6 targets and `gateway6`.
  - `Security/` — `PasswordHash` (PBKDF2-SHA256 `pbkdf2-sha256$iter$salt$key` or plain, constant-time),
    `LoginThrottle` (per-client failure window).
  - `Connection/` — `LinkInfo` (`LinkType` Wired/WiFi/Unknown, SSID, signal dBm, speed), `WifiSignal` (rating,
    Windows % → dBm), `WirelessParsers` (`/proc/net/wireless`, `iw dev X link`, `netsh wlan` in any language),
    `LinkHistory` (link at a moment), `LinkDiagnosis` (Wi-Fi vs router vs ISP hint for an outage).
  - `Targets/` — `TargetName` (validation/normalization), `TargetManager` (configured + custom targets).
  - `Qr/` — `QrCode` (byte mode, versions 1–10, all ECC levels, checked against Nayuki's reference
    encoder), `QrSvg` (path data, quiet zone); `ReedSolomon` (GF(256)).
  - `ClientCheck/` — check run from another device's browser: `LanUrl` (address phones open, from Kestrel
    bindings + LAN IP), `ClientCheckMeasurement` (sanitized input), `HostInternet`, `ClientCheckDiagnosis`
    (verdict + advice), `ClientCheckLog` (recent checks, in memory).
  - `LoadTest/` — cooperative load-test logic: `LoadTestRequest` (validation + safety caps),
    `LoadTestStats`, `ThroughputSample`, `RatePacing` (bandwidth pacing), `LoadTestWire`
    (dependency-free binary framing). All pure and unit-tested; the sockets live in Web.
- `src/NetPulse.Web` — ASP.NET Core host. Pages: `/` (latency), `/speed`, `/outages`, `/devices` (LAN devices), `/loadtest`, `/report`, `/check` (phone check + QR)
  (printable; `@media print` in `app.css`).
  - `Monitoring/PingMonitorService` — `BackgroundService` that probes all targets every N seconds and feeds
    `OutageTracker`; open outages from a previous run are closed at startup.
  - `Monitoring/OutageTracer` — traceroute in the background when an outage starts (one at a time), stored by
    `Storage/SqliteOutageTraceStore` (hops as JSON); shown on `/outages`, in the report and `/api/outages`.
  - `Health/` — `MonitorHealthCheck` (heartbeat) and `DatabaseHealthCheck` behind `/healthz`; `/metrics` in `Program.cs`.
  - `SpeedTest/` — `SpeedTestRunner` (one test at a time, `Changed` event), `SpeedTestService` (scheduler).
  - `Reports/ReportService` — streams samples (`ISampleStore.StreamAsync`) into the report; CSV endpoints in `Program.cs`.
  - `Discovery/LanDiscovery` + `LanDiscoveryService` — runs scans (periodic and "Scan now").
  - `LoadTest/` — `LoadTestReceiver` (opt-in `BackgroundService`, disabled by default; the target/agent),
    `LoadTestClient` (sender), `LoadTestManager` (orchestrates one run, live samples + `Changed` event).
  - `Notifications/` — `NotificationOptions`, `NotificationChannels` (builds the configured ones), `EmailChannel`
    (SMTP), `NotificationDispatcher` (publisher + `BackgroundService`: batches, delivers, retries; test endpoint).
    Published from `PingMonitorService`, `LanDiscovery` and `SpeedTestRunner`.
  - `Retention/DataRetentionService` — the only place that purges old data (all stores, hourly) and compacts
    via `Storage/SqliteMaintenance`; `RetentionStatus` feeds the Report page card and `/api/retention`.
  - `Security/` — `DashboardAuth` (optional, `NetPulse:Auth`): cookie sign-in + `ApiTokenHandler` (Bearer)
    behind a fallback "authenticated" policy; `/login` (static SSR page, see `App.razor` render mode),
    `POST /auth/login|logout`; `HashPasswordCommand` (`NetPulse.Web hash-password`). Open endpoints must
    call `.AllowAnonymous()`.
  - `Connection/LinkDetector` (default-route interface, `/sys`, `iw`, `netsh`) + `LinkMonitor` (`BackgroundService`,
    every 30 s, stores a `link_samples` row a minute via `Storage/SqliteLinkStore`); `Shared/ConnectionCard` on `/`,
    Connection column on `/outages` and in the report.
  - `ClientCheck/ClientCheckEndpoints` — `/api/check/*` (ping, download, upload, result); the `/check` page is
    static SSR (like `/login`, see `App.razor`) with `wwwroot/check.js`, the only script; `ClientCheckHistory`
    is an interactive island. Nav link uses `target="_top"` so the interactive router does a full load.
  - `Storage/Sqlite*Store` — `Microsoft.Data.Sqlite`, raw SQL, timestamps as Unix ms UTC.
  - `Storage/SqliteTargetStore` — custom targets added from the dashboard (same database file).
  - `Program.cs` — DI, minimal API (`/api/samples`, `/api/stats`, `/api/outages`, `/api/speedtests`, `/api/report`, `/api/export/*.csv|json`, `/api/targets`, `/api/devices`, `/api/loadtest`), Blazor (Interactive Server).
  - `Components/` — dashboard; `Shared/AppHeader` (logo, `SiteNav`, `ThemePicker`) is the top bar of every page;
    themes are token blocks in `wwwroot/themes.css` (`html[data-theme]`, set from a cookie in `App.razor`
    via `GET /theme/{id}`, ids validated by `Core/Appearance/ThemeCatalog`); `app.css` only reads tokens.
    `docs/dashboard.css`/`docs/themes.css` are copies for the static demo; `LatencyChart`/`ThroughputChart`/`SpeedChart`/`OutageTimeline` are dependency-free SVG charts;
    `TargetEditor` adds/removes custom targets.
- `tests/NetPulse.Tests` — NUnit 4, constraint model (`Assert.That`).
- `.github/workflows/release.yml` — on `v*` tags, self-contained single-file archives for win-x64,
  linux-x64 and linux-arm64 attached to a GitHub release.

## Conventions

- Code, comments, commit messages and README in **English**.
- Keep `NetPulse.Core` free of ASP.NET and database dependencies; anything testable goes there.
- New logic in Core needs NUnit tests.
- Format numbers in markup/SVG with `CultureInfo.InvariantCulture` (the author's machine is it-IT).
- Prefer no JavaScript and no heavy UI libraries; keep the dashboard self-contained.
- Small, focused commits with clear messages (Conventional Commits: `feat:`, `fix:`, `test:`, `docs:`).

## Roadmap

- **v0.2 — LAN discovery**: scan the local subnet (ping sweep + ARP table), show IP, MAC,
  vendor (OUI lookup from a bundled file), hostname (reverse DNS); detect new/offline devices.
- **v0.3 — Speed tests & outages**: scheduled download/upload test against a free public endpoint;
  detect outages (N consecutive losses) and show them on a timeline.
- **v1.0 — Reports**: CSV export, then a printable report for the ISP; release binaries via
  GitHub Actions (self-contained for win-x64, linux-x64, linux-arm64).

## Notes

- ICMP on Linux: unprivileged ping usually works (Docker enables `ping_group_range`); if not,
  document `--cap-add NET_RAW`.
- `Ping.RoundtripTime` has 1 ms resolution; that is acceptable for this use case.
- Speed tests default to Cloudflare's free endpoint (`speed.cloudflare.com/__down`, `__up`); URLs are configurable.
- `MonitorOptions.Targets` defaults to an empty list on purpose (the config binder appends to
  lists); `EffectiveTargets` applies the defaults.
