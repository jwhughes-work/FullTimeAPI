# ⚽ FullTime API

**An unofficial REST API for grassroots football data from [FA Full-Time](https://fulltime.thefa.com/).**

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/9.0)
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
- 📈 **Form Guide** — a team's last 5 results (W/D/L)
- 🏆 **League Tables** — full standings, plus a 3-team "snapshot" centred on any team
- 👤 **Player Stats** — appearances, goals, and cards by FA player ID
- ⚡ **Built-in caching** — responses cached in memory (~30 min) to keep things fast and be kind to the FA's servers
- 🛡️ **Rate limiting & resilience** — IP rate limiting and Polly retry policies out of the box
- 🌐 **CORS enabled** — call it straight from your frontend

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
| `/api/Results/{divisionId}?teamName={name}` | Match results for a division (optional team filter) |
| `/api/Results/{divisionId}/form?teamName={name}` | A team's last 5 results — form guide (`W`, `D`, `L`, `P`) |

### League Tables

| Endpoint | Description |
|---|---|
| `/api/League/{divisionId}` | Full league standings for a division |
| `/api/League/{divisionId}/snapshot?teamName={name}` | Mini-table: the team above, the given team, and the team below |

### Players

| Endpoint | Description |
|---|---|
| `/api/Player/{faPlayerId}` | Player statistics — appearances, goals, and cards |

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
- An internet connection (data is fetched live from [FA Full-Time](https://fulltime.thefa.com/))

### Run locally

```bash
# Clone the repository
git clone https://github.com/jwhughes-work/FullTimeAPI.git
cd FullTimeAPI

# Build and run
dotnet build
dotnet run --project FullTimeAPI
```

The API starts on the configured port and serves Swagger UI at `/swagger` — open it in your browser to explore and test every endpoint.

## 🏗️ How It Works

FullTime API is an ASP.NET Core Web API that scrapes the FA Full-Time website on demand using [HtmlAgilityPack](https://html-agility-pack.net/), parses the HTML into typed models, and serves them as JSON.

```text
Client ──▶ Controller ──▶ Service ──▶ IMemoryCache (hit? return)
                              │
                              └──▶ FA Full-Time (resilient HttpClient + Polly retries)
```

| Concern | Implementation |
|---|---|
| Framework | ASP.NET Core (.NET 9) |
| Scraping | HtmlAgilityPack |
| Caching | In-memory, ~30 minute TTL per query |
| Resilience | Polly retry policies via `IHttpClientFactory` |
| Rate limiting | AspNetCoreRateLimit (per-IP: 300/min, 500/15min, 1000/hr) |
| Docs | Swagger / Swashbuckle with XML comments |
| Errors | Global exception-handling middleware |

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
