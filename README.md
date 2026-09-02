# ⚽ FullTime API

**An unofficial REST API for grassroots football data from [FA Full-Time](https://fulltime.thefa.com/).**

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Playwright](https://img.shields.io/badge/Playwright-Headless%20Chromium-2EAD33?logo=playwright&logoColor=white)](https://playwright.dev/dotnet/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](#-license)
[![Live Demo](https://img.shields.io/badge/Live%20Demo-Swagger-85EA2D?logo=swagger&logoColor=black)](https://faapi.jwhsolutions.co.uk/swagger/index.html)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](#-contributing)

FullTime API turns the FA's Full-Time website into clean, structured JSON. It scrapes fixtures, results, league tables, and player statistics for grassroots leagues across England — perfect for building club websites, team apps, Discord bots, or match-day dashboards.

> **Disclaimer:** This is an unofficial API and is not endorsed by or affiliated with The Football Association (The FA). All data belongs to its respective owners.

---

## 🚀 Try It Now

No setup required — a live instance with interactive Swagger docs is available here:

**👉 [faapi.jwhsolutions.co.uk/swagger](https://faapi.jwhsolutions.co.uk/swagger/index.html)**

```bash
# Search for a league
curl "https://faapi.jwhsolutions.co.uk/api/Search/leagues/Weston"

# Get a division's league table
curl "https://faapi.jwhsolutions.co.uk/api/League/{divisionId}"
```

---

## ✨ Features

- 🔍 **Search** — find leagues, divisions, clubs, and teams by name
- 📅 **Fixtures** — upcoming fixtures for a division, filterable by team
- 🏁 **Results** — completed match results, filterable by team
- 🗓️ **Season filters** — fixtures, results, and tables for past seasons, not just the current one
- 📈 **Form Guide** — a team's last 5 results (W/D/L)
- 🏆 **League Tables** — full standings, plus a 3-team "snapshot" centred on any team
- 👤 **Player Stats** — appearances, goals, assists, and cards by FA player ID
- 🌐 **Headless-browser scraping** — pages are fetched with real Chromium via [Playwright](https://playwright.dev/dotnet/), which gets past the Cloudflare TLS fingerprinting that blocks plain HTTP clients
- ⚡ **Built-in caching** — responses cached in memory (30 min) to keep things fast and be kind to the FA's servers
- 🛡️ **Rate limiting & resilience** — per-IP rate limiting and Polly retry policies out of the box
- 🔓 **CORS enabled** — call it straight from your frontend

## 📖 API Reference

All endpoints are `GET` and return JSON. Full interactive documentation is available via [Swagger](https://faapi.jwhsolutions.co.uk/swagger/index.html).

### Search

| Endpoint | Description |
|---|---|
| `/api/Search/leagues/{leagueName}` | Search leagues by (partial) name → returns `LeagueId` |
| `/api/Search/divisions/{leagueId}` | List divisions in a league → returns `DivisionId` |
| `/api/Search/clubs/{clubName}` | Search clubs by (partial) name → returns `ClubId` |
| `/api/Search/teams/{clubId}` | List all teams within a club |

### Fixtures & Results

| Endpoint | Description |
|---|---|
| `/api/Fixtures/{divisionId}?teamName={name}` | Upcoming fixtures for a division (optional team filter) |
| `/api/Fixtures/{divisionId}/season/{season}?teamName={name}` | Fixtures for a specific season |
| `/api/Results/{divisionId}?teamName={name}` | Match results for a division (optional team filter) |
| `/api/Results/{divisionId}/season/{season}?teamName={name}` | Results for a specific season |
| `/api/Results/{divisionId}/form?teamName={name}` | A team's last 5 results — form guide (`W`, `D`, `L`, `P`) |

### League Tables

| Endpoint | Description |
|---|---|
| `/api/League/{divisionId}` | Full league standings for a division |
| `/api/League/{divisionId}/season/{season}` | League standings for a specific season |
| `/api/League/{divisionId}/snapshot?teamName={name}` | Mini-table: the team above, the given team, and the team below |

### Players

| Endpoint | Description |
|---|---|
| `/api/Player/{faPlayerId}` | Player statistics — appearances, goals, assists, and cards |

> **About `{season}`:** the value is passed straight through to FA Full-Time as its `selectedSeason` query parameter, so use the same season ID that appears in the season dropdown on the corresponding Full-Time page. Omit the `/season/{season}` segment to get the current season.

### Typical workflow

Most endpoints are keyed by **division ID**, which you can discover through search:

```text
1. GET /api/Search/leagues/Weston        → pick a league, note its LeagueId
2. GET /api/Search/divisions/{leagueId}  → pick a division, note its DivisionId
3. GET /api/Fixtures/{divisionId}        → fixtures, results, tables, etc.
```

### Example response

`GET /api/Fixtures/{divisionId}`

```json
[
  {
    "fixtureDateTime": "15/03/25 14:00",
    "homeTeam": "Axbridge Town",
    "awayTeam": "Cheddar FC",
    "location": "Axbridge Playing Fields",
    "competition": "Premier Division"
  }
]
```

## 🏃 Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- [PowerShell 7+ (`pwsh`)](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) — needed **once**, to run Playwright's browser installer. Playwright for .NET ships a `playwright.ps1` script and has no shell-script equivalent, so `pwsh` is required on every platform, Linux and macOS included. If you don't have it: `dotnet tool install --global PowerShell`
- An internet connection (data is fetched live from [FA Full-Time](https://fulltime.thefa.com/))
- A few hundred MB of free disk for the Chromium build that Playwright downloads

### Run locally

```bash
# Clone the repository
git clone https://github.com/jwhughes-work/FullTimeAPI.git
cd FullTimeAPI

# Build (this also generates playwright.ps1 into the output directory)
dotnet build

# One-time: download the headless Chromium that Playwright drives.
# On Linux, add --with-deps to also install Chromium's OS-level dependencies:
#   pwsh FullTimeAPI/bin/Debug/net9.0/playwright.ps1 install --with-deps chromium
pwsh FullTimeAPI/bin/Debug/net9.0/playwright.ps1 install chromium

# Run
dotnet run --project FullTimeAPI
```

The default `http` launch profile serves the API on <http://localhost:5069> and opens Swagger UI at `/swagger` — explore and test every endpoint from there. Use `dotnet run --project FullTimeAPI --launch-profile https` to additionally bind <https://localhost:7128>.

> **Skipping the browser install?** The app builds and starts fine without it, but the first request that hits FA Full-Time will fail when Playwright can't find a Chromium executable. Run the `playwright.ps1 install` step once and it's done for good.

## 🏗️ How It Works

FullTime API is an ASP.NET Core Web API. Requests are served from an in-memory cache where possible; on a miss the relevant FA Full-Time page is fetched with a **headless Chromium browser** driven by [Microsoft.Playwright](https://playwright.dev/dotnet/), parsed into typed models with [HtmlAgilityPack](https://html-agility-pack.net/), then cached and returned as JSON.

```text
Client ──▶ Controller ──▶ Service ──▶ IMemoryCache ──▶ hit? return JSON
                                          │ miss
                                          ▼
                              IPageFetcher (PlaywrightPageFetcher)
                                          │  Polly: 3 retries, exponential backoff
                                          ▼
                              Headless Chromium ──▶ fulltime.thefa.com
                                          │
                                          ▼
                    HtmlAgilityPack ──▶ typed model ──▶ cache (30 min) ──▶ JSON
```

### Why a headless browser?

FA Full-Time sits behind Cloudflare, which fingerprints the **TLS handshake itself**. .NET's `HttpClient` gets a `403` regardless of the headers it sends — setting a browser `User-Agent` doesn't help, because the block happens below HTTP, before a single header is read. `curl` and real browsers get through; `HttpClient` doesn't.

Rather than trying to imitate a browser, the API now uses one. Every page fetch goes through `PlaywrightPageFetcher`, so the handshake genuinely *is* Chromium's. This replaced the previous `IHttpClientFactory` + `HttpClientExtensions` scraping path entirely.

### Fetching details

- **One shared browser.** `PlaywrightPageFetcher` is registered as a singleton and launches Chromium lazily on the first request (guarded by a `SemaphoreSlim`), so the browser start-up cost is paid once rather than per request. Each fetch then gets its own fresh `BrowserContext` — an isolated cookie/cache jar — which is disposed as soon as the page has been read.
- **Assets are blocked.** Images, stylesheets, fonts, and media are aborted at the router, since only the HTML is needed. Each page load navigates with `DOMContentLoaded` and a 30-second timeout.
- **Retries and bounce detection.** A Polly policy retries 3 times with exponential backoff (2s, 4s, 8s) on exceptions, non-success statuses, *and* "bounced" responses — Full-Time redirects an unrecognised division ID to `/home`, so `PageFetchResult.LooksBounced` compares the requested path against the final URL and treats a mismatch as retryable. Failures are logged with the status, final URL, and a body snippet, so a genuinely empty division can be told apart from a block page.
- **`--no-sandbox`.** Chromium is launched with the sandbox disabled: it needs unprivileged user namespaces (restricted on many VPS kernels and containers), and refuses to start as root at all — which is common for bare systemd deployments.

| Concern | Implementation |
|---|---|
| Framework | ASP.NET Core (.NET 9) |
| Page fetching | Headless Chromium via `Microsoft.Playwright` |
| HTML parsing | HtmlAgilityPack |
| Caching | In-memory, 30 minute TTL per query |
| Resilience | Polly retry policies inside the page fetcher |
| Rate limiting | AspNetCoreRateLimit (per-IP: 300/min, 500/15min, 1000/hr) |
| Docs | Swagger / Swashbuckle with XML comments |
| Errors | Global exception-handling middleware |

## 🚢 Deployment notes

Because scraping now runs a real browser, a deployed instance needs a little more than a plain .NET app:

- **Install the browser on the server too.** Publishing the app doesn't bring Chromium with it. Run the installer once against the published output, e.g. `pwsh playwright.ps1 install --with-deps chromium`. On Linux, `--with-deps` pulls in the system libraries Chromium needs — without them it fails to start with missing-shared-library errors.
- **Give it enough memory.** A headless Chromium instance needs noticeably more RAM than an `HttpClient` did; a 512 MB container is tight.
- **Docker.** Either run the install step in your `Dockerfile` after `dotnet publish`, or base your image on one of the [Playwright .NET images](https://playwright.dev/dotnet/docs/docker), which ship the browsers and dependencies pre-installed.
- **Running as root** (typical for systemd units and slim containers) is already handled by the `--no-sandbox` launch flag.

## 🤝 Contributing

Contributions are welcome! If you'd like to improve the API or add new features:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes and push the branch
4. Open a pull request

Found a bug or have an idea? [Open an issue](https://github.com/jwhughes-work/FullTimeAPI/issues) — all suggestions are appreciated.

## 🙏 Acknowledgements

- **[FA Full-Time](https://fulltime.thefa.com/)** — the source of all data, provided by The FA
- **[jadgray/FullTimeApi](https://github.com/jadgray/FullTimeApi)** — the original project that inspired and provided the starting point for this one

## 📄 License

This project is open source and available under the [MIT License](https://opensource.org/licenses/MIT).

---

<p align="center">
  Enjoying FullTime API? Give it a ⭐ — and if you have questions or feedback, <a href="https://github.com/jwhughes-work/FullTimeAPI/issues">open an issue</a>!
</p>
