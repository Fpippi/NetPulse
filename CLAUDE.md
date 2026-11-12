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
  - `Storage/ISampleStore`, `Storage/ITargetStore` — persistence abstractions.
  - `Targets/` — `TargetName` (validation/normalization), `TargetManager` (configured + custom targets).
- `src/NetPulse.Web` — ASP.NET Core host.
  - `Monitoring/PingMonitorService` — `BackgroundService` that pings all targets every N seconds.
  - `Storage/SqliteSampleStore` — `Microsoft.Data.Sqlite`, raw SQL, timestamps as Unix ms UTC.
  - `Storage/SqliteTargetStore` — custom targets added from the dashboard (same database file).
  - `Program.cs` — DI, minimal API (`/api/samples`, `/api/stats`, `/api/targets`), Blazor (Interactive Server).
  - `Components/` — dashboard; `LatencyChart` is a dependency-free SVG chart;
    `TargetEditor` adds/removes custom targets.
- `tests/NetPulse.Tests` — NUnit 4, constraint model (`Assert.That`).

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
- `MonitorOptions.Targets` defaults to an empty list on purpose (the config binder appends to
  lists); `EffectiveTargets` applies the defaults.
