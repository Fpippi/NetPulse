# NetPulse

[![CI](https://github.com/Fpippi/NetPulse/actions/workflows/ci.yml/badge.svg)](https://github.com/Fpippi/NetPulse/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

**Is your connection really stable?** NetPulse is a free, open-source, self-hosted monitor that
continuously pings your router and the Internet, stores the results and shows latency, jitter and
packet loss on a live dashboard — so you can tell whether drops come from your Wi-Fi, your router
or your ISP.

<p align="center">
  <a href="https://fpippi.github.io/NetPulse/demo.html">
    <img src="docs/demo.gif" width="760" alt="NetPulse dashboard: per-target latency, jitter and packet loss with a live scrolling chart">
  </a>
</p>

<p align="center">
  <b><a href="https://fpippi.github.io/NetPulse/">Project page</a></b> ·
  <b><a href="https://fpippi.github.io/NetPulse/demo.html">Live demo</a></b>
  (simulated data, runs in your browser)
</p>

## Contents

- [Features](#features)
- [Quick start](#quick-start)
- [Configuration](#configuration)
- [API](#api)
- [Architecture](#architecture)
- [Development](#development)
- [Roadmap](#roadmap)

## Features

- Continuous ping to the **default gateway** (auto-detected) and to public hosts (1.1.1.1, 8.8.8.8)
- **Latency, jitter, packet loss** and a simple quality rating (Good / Fair / Poor) per target
- Live **Blazor dashboard** with 15m / 1h / 24h views; spikes and lost packets stay visible on the chart
- **REST API** (`/api/samples`, `/api/stats`) for scripts and integrations
- Local **SQLite** storage with automatic retention — no cloud, no account, no tracking
- Runs on **Windows, Linux, macOS** and in **Docker** (Raspberry Pi included)

## Quick start

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
git clone https://github.com/Fpippi/NetPulse.git
cd NetPulse
dotnet run --project src/NetPulse.Web
```

Open <http://localhost:5080>. The first samples appear after a few seconds.

### Docker

```bash
docker build -t netpulse .
docker run -d --name netpulse -p 8080:8080 -v netpulse-data:/data netpulse
```

Open <http://localhost:8080>.

> On Linux, use `--network host` if you want the gateway seen by the container to be your real router.
> If pings always time out, the container may not be allowed to send ICMP: add `--cap-add NET_RAW`.

## Configuration

Edit `src/NetPulse.Web/appsettings.json` or use environment variables, e.g. `NetPulse__IntervalSeconds=10`
or, for lists, `NetPulse__Targets__0=gateway` and `NetPulse__Targets__1=192.168.1.10`.

| Key               | Default                           | Description                                   |
|-------------------|-----------------------------------|-----------------------------------------------|
| `IntervalSeconds` | `5`                               | Time between ping rounds                      |
| `TimeoutMs`       | `1000`                            | Ping timeout                                  |
| `RetentionDays`   | `30`                              | Older samples are deleted automatically       |
| `DatabasePath`    | `data/netpulse.db`                | SQLite file                                   |
| `Targets`         | `gateway`, `1.1.1.1`, `8.8.8.8`   | Hosts to monitor (`gateway` = default router) |

## API

| Endpoint                                 | Description                       |
|------------------------------------------|-----------------------------------|
| `GET /api/samples?minutes=15&target=...` | Raw samples, optionally for one target |
| `GET /api/stats?minutes=60`              | Aggregated stats and quality per target |

`minutes` defaults to 15 and is clamped between 1 minute and 7 days.

## Architecture

```
src/
  NetPulse.Core   Domain: models, ping probe, statistics, storage abstraction (no dependencies)
  NetPulse.Web    ASP.NET Core host: background monitor, SQLite store, REST API, Blazor dashboard
tests/
  NetPulse.Tests  NUnit tests for statistics and chart downsampling
docs/             Project page and interactive demo (GitHub Pages)
```

## Development

```bash
dotnet build      # build everything
dotnet test       # run the NUnit tests
```

CI builds and tests every pull request and every push to `main` and `develop`, on Linux and Windows.

## Roadmap

- [x] **v0.1** — Ping monitor, SQLite storage, dashboard, API
- [ ] **v0.2** — LAN device discovery (IP, MAC, vendor, hostname) and online/offline alerts
- [ ] **v0.3** — Scheduled speed tests, outage detection and timeline
- [ ] **v1.0** — Exportable reports (CSV/PDF) to send to your ISP

## Project page

The [project page](https://fpippi.github.io/NetPulse/) and the interactive demo live in [`docs/`](docs):
plain HTML, CSS and a small script that simulates samples in the browser with the same statistics as
`NetPulse.Core`. The `Pages` workflow publishes them to GitHub Pages on every push to `main` that touches
`docs/`.

## Contributing

Issues and pull requests are welcome.

## License

[MIT](LICENSE)
