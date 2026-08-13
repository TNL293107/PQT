# Personal Quantitative Trading & Market Intelligence Terminal

A Bloomberg-inspired quantitative research and trading workstation, built as a
long-term personal engineering project.

> **Current phase: Phase 0 — Foundation & Architecture.**
>
> This repository contains a working, tested engineering foundation and **no
> financial functionality**. There is no market data, no research, no
> backtesting, no portfolio, no trading, and no AI. The
> [roadmap](docs/roadmap/phases.md) describes where those arrive.

---

## Goals

Build the closed loop that separates a research platform from a chart viewer:

```
market data → research → signal → backtest → risk → execution → monitoring
     ▲                                                              │
     └──────────────────── analysis feeds the next hypothesis ──────┘
```

The system is data-first. The terminal UI is a view onto the domain, not the
place behaviour lives — a chart on top of a model that cannot distinguish
`AAPL` from `AAPL.US` is worth nothing.

## Architecture

```
                        ┌───────────┐
                        │  Browser  │
                        └─────┬─────┘
                              │
                    ┌─────────▼──────────┐
                    │  Frontend (nginx)  │   React 19 + TypeScript
                    │  :3000             │
                    └─────────┬──────────┘
                              │  /health, /health/ready
                    ┌─────────▼──────────┐
                    │  Backend API       │   ASP.NET Core 10
                    │  :8080             │   Modular monolith
                    └─────────┬──────────┘
              ┌───────────────┴───────────────┐
              ▼                               ▼
     ┌─────────────────┐            ┌──────────────────┐
     │   PostgreSQL    │            │      Redis       │
     │   :5432         │            │      :6379       │
     └─────────────────┘            └──────────────────┘

     ┌─────────────────┐            ┌──────────────────┐
     │  Python quant   │            │   C++ engine     │
     │  (toolchain)    │            │   (toolchain)    │
     └─────────────────┘            └──────────────────┘
```

A **modular monolith**, not microservices — see
[ADR-001](docs/architecture/decisions/ADR-001-modular-monolith.md). The Python
and C++ layers build and test independently but are not yet wired to the
backend; establishing them before there is pressure to use them is the point of
Phase 0.

## Technology stack

| Layer       | Technology                                       |
| ----------- | ------------------------------------------------ |
| Backend     | C# 14, ASP.NET Core 10 (LTS), EF Core 10          |
| Frontend    | React 19, TypeScript 6, Vite 8, Vitest            |
| Database    | PostgreSQL 17                                     |
| Cache       | Redis 8                                           |
| Quant       | Python 3.12+, pytest, ruff, mypy (strict)         |
| Performance | C++20, CMake, GoogleTest, CTest                   |
| Containers  | Docker Compose                                    |
| CI          | GitHub Actions                                    |

## Repository structure

```
.
├── .github/workflows/       CI for all four stacks
├── backend/                 ASP.NET Core modular monolith
│   ├── src/
│   │   ├── PersonalQuant.Api/              HTTP, middleware, health, OpenAPI
│   │   ├── PersonalQuant.Application/      use cases, ports
│   │   ├── PersonalQuant.Domain/           financial model (empty in Phase 0)
│   │   └── PersonalQuant.Infrastructure/   PostgreSQL, Redis, migrations
│   └── tests/               unit + integration
├── frontend/                React terminal
├── quant/                   Python research layer
├── cpp-engine/              C++ performance layer
├── data/                    schemas and fixtures (bulk data git-ignored)
├── docs/
│   ├── architecture/        overview, context, data policy, ADRs
│   ├── development/         local setup, git workflow
│   └── roadmap/             phases
├── docker-compose.yml
└── .env.example
```

## Quick start

```bash
cp .env.example .env
```

Edit `.env` and replace `CHANGE_ME` with a local password, then:

```bash
docker compose up --build
```

| Service    | URL                                |
| ---------- | ---------------------------------- |
| Terminal   | http://localhost:3000              |
| Liveness   | http://localhost:8080/health       |
| Readiness  | http://localhost:8080/health/ready |
| OpenAPI UI | http://localhost:8080/scalar/v1    |

Full instructions, including running services on the host for a faster edit
loop, are in [docs/development/local-setup.md](docs/development/local-setup.md).

## What actually works today

Everything in this list is implemented and covered by tests.

- **`GET /health`** — liveness. Touches no external dependency, so a database
  outage cannot cause a supervisor to restart a healthy process.
- **`GET /health/ready`** — readiness. Executes a real round-trip query against
  PostgreSQL and a `PING` against Redis, and reports each separately. Returns
  503 when a dependency is unavailable, and discloses no host, port, user or
  driver detail while doing so.
- **EF Core migration pipeline** — a baseline migration creates the `quant`
  schema and the migrations history table.
- **Terminal system status page** — live per-service state with real loading
  and error handling.
- **Four independent test suites** — .NET, Vitest, pytest, CTest.
- **Docker Compose environment** — four services with health-gated
  dependencies and persistent volumes.

## Testing

```bash
dotnet test backend/PersonalQuant.slnx
```

```bash
npm ci --prefix frontend && npm run lint --prefix frontend && npm test --prefix frontend
```

```bash
cd quant && pytest && ruff check . && mypy
```

```bash
cd cpp-engine && cmake --preset ci && cmake --build --preset ci && ctest --preset ci
```

The backend integration tests start real PostgreSQL and Redis containers via
Testcontainers. Without Docker they **skip with an explicit reason** rather
than passing quietly.

## Security

Phase 0 establishes hygiene, not a security model.

- No secret is committed. `.env` is git-ignored; `.env.example` holds empty
  placeholders.
- Credentials come from the environment. No committed file contains a password.
- Connection strings are built with `NpgsqlConnectionStringBuilder` — no value
  is ever concatenated into one.
- CORS is restricted to configured origins. An unset value permits no browser
  origin, never all of them.
- Configuration is validated at start-up, so a misconfigured deployment fails
  fast instead of serving requests.
- Health endpoints log failures in full and disclose nothing to the caller.
- CI fails the build on a dependency with a known advisory, and scans history
  for secrets.
- `LIVE_TRADING_ENABLED` defaults to `false` and stays false until Phase 13.

**There is no authentication yet.** There is nothing to protect, and guessing
the model now would be rework. It arrives in Phase 17, or sooner if any part of
this leaves localhost.

## Data policy

Market data is licensed, not owned. This repository's licence covers its source
code and says nothing about data the software may one day retrieve.

No vendor data, no dataset, and no provider key exists in this repository. Any
future provider must be evaluated against the checklist in
[docs/architecture/data-policy.md](docs/architecture/data-policy.md) — API
terms, redistribution rights, storage rights, and commercial-use rights — before
integration.

## Roadmap

| Tier | Becomes           | Phases | Status   |
| ---- | ----------------- | ------ | -------- |
| —    | Foundation        | 0      | COMPLETE |
| 1    | Research terminal | 1–6    | PLANNED  |
| 2    | Quant platform    | 7–10   | PLANNED  |
| 3    | Trading system    | 11–14  | PLANNED  |
| 4    | Engineered system | 15–18  | PLANNED  |

Next up is **Phase 1 — Instrument Master**: know what an instrument *is* before
storing anything about it. Done when searching `NVDA` resolves to exactly one
security, and `AAPL`, `AAPL.US`, `US0378331005` and `BBG000B9XRY4` all resolve
to the same canonical ID.

Full detail in [docs/roadmap/phases.md](docs/roadmap/phases.md).

## Design constraints decided up front

Recorded now because they are expensive to retrofit:

- **Canonical instrument identity.** Provider symbols are aliases, never keys.
- **Point-in-time correctness.** Every fact carries both the period it
  describes and the moment it became knowable, so backtests cannot use
  information that did not exist yet.
- **Data quality is a pipeline stage**, not an assumption.
- **Deterministic execution path.** `strategy → signal → portfolio → risk → OMS
  → execution → broker`. A strategy has no route to a broker; the risk engine
  can reject.
- **AI advises, never trades.** The Phase 16 analyst has no path to the OMS.

## Documentation

| Document                                                                       | Contents                        |
| ------------------------------------------------------------------------------ | ------------------------------- |
| [Architecture overview](docs/architecture/overview.md)                         | Current and target architecture |
| [System context](docs/architecture/system-context.md)                          | Actors and external systems     |
| [Data policy](docs/architecture/data-policy.md)                                | Market data licensing           |
| [ADRs](docs/architecture/decisions/)                                           | Seven recorded decisions        |
| [Local setup](docs/development/local-setup.md)                                 | Build, run, test, troubleshoot  |
| [Git workflow](docs/development/git-workflow.md)                               | Branching and commit standards  |
| [Roadmap](docs/roadmap/phases.md)                                              | All nineteen phases             |

## License

**Proprietary — all rights reserved.** This is a private repository and is
**not** an open-source project. See [LICENSE.md](LICENSE.md).

Third-party dependencies remain under their own licences. Market data is
governed by provider terms, not by this licence.

Nothing produced by this software is financial advice.
