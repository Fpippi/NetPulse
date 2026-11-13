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
  - `Probing/` — `IPingProbe` + `IcmpPingProbe`, `GatewayResolver`.
  - `Stats/` — `LatencyStats` (avg/min/max/jitter/loss/quality), `ChartDownsampler`.
  - `Storage/I*Store` — persistence abstractions (samples, targets, outages, speed tests, LAN devices).
  - `Outages/` — `OutageTracker` (N consecutive lost pings → `Outage`, ends at the next reply),
    `OutageSummary` (downtime/availability over a window), `DurationFormat`.
  - `SpeedTest/` — `HttpSpeedTest` (latency/download/upload over `HttpClient`), `SpeedTestPlan`
    (validation), `TransferMeter` (Mbps with TCP warm-up skipped), `SpeedTestSchedule`, `SpeedTestSummary`.
  - `Reports/` — `IspReportBuilder` (→ `IspReport`), `ReportPeriod` (days or from/to dates), `Csv` +
    `CsvExport` (RFC 4180, invariant numbers, UTC ISO 8601). `Outages/OutageClassifier` labels outages
    LocalNetwork / Internet / SingleHost / Unknown; `Stats/LatencyAccumulator` streams `LatencyStats`.
  - `Targets/` — `TargetName` (validation/normalization), `TargetManager` (configured + custom targets).
  - `LoadTest/` — cooperative load-test logic: `LoadTestRequest` (validation + safety caps),
    `LoadTestStats`, `ThroughputSample`, `RatePacing` (bandwidth pacing), `LoadTestWire`
    (dependency-free binary framing). All pure and unit-tested; the sockets live in Web.
- `src/NetPulse.Web` — ASP.NET Core host. Pages: `/` (latency), `/speed`, `/outages`, `/devices` (LAN devices), `/loadtest`, `/report`
  (printable; `@media print` in `app.css`).
  - `Monitoring/PingMonitorService` — `BackgroundService` that pings all targets every N seconds and feeds
    `OutageTracker`; open outages from a previous run are closed at startup.
  - `SpeedTest/` — `SpeedTestRunner` (one test at a time, `Changed` event), `SpeedTestService` (scheduler).
  - `Reports/ReportService` — streams samples (`ISampleStore.StreamAsync`) into the report; CSV endpoints in `Program.cs`.
  - `Discovery/LanDiscovery` + `LanDiscoveryService` — runs scans (periodic and "Scan now").
  - `LoadTest/` — `LoadTestReceiver` (opt-in `BackgroundService`, disabled by default; the target/agent),
    `LoadTestClient` (sender), `LoadTestManager` (orchestrates one run, live samples + `Changed` event).
  - `Storage/Sqlite*Store` — `Microsoft.Data.Sqlite`, raw SQL, timestamps as Unix ms UTC.
  - `Storage/SqliteTargetStore` — custom targets added from the dashboard (same database file).
  - `Program.cs` — DI, minimal API (`/api/samples`, `/api/stats`, `/api/outages`, `/api/speedtests`, `/api/report`, `/api/export/*.csv`, `/api/targets`, `/api/devices`, `/api/loadtest`), Blazor (Interactive Server).
  - `Components/` — dashboard; `LatencyChart`/`ThroughputChart`/`SpeedChart`/`OutageTimeline` are dependency-free SVG charts;
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
