# Architecture Overview

**Phase: 0 — Foundation & Architecture.** This document describes what exists
today and the direction it is built to grow in. Anything marked PLANNED is not
implemented.

## Purpose

A personal quantitative research and trading workstation. The long-term goal is
a closed loop:

```
market data → research → signal → backtest → risk → execution → monitoring
     ▲                                                              │
     └──────────────────── analysis feeds the next hypothesis ──────┘
```

The system is data-first, not UI-first. The terminal is a view onto the data
and the domain logic; it is not where behaviour lives. A chart that looks
right on top of an instrument model that cannot tell `AAPL` from `AAPL.US` is
worth nothing.

## What exists today

```
                        ┌───────────┐
                        │  Browser  │
                        └─────┬─────┘
                              │ HTTP
                    ┌─────────▼──────────┐
                    │   Frontend (nginx) │   React 19 + TypeScript
                    │   :3000            │   System status, capability map
                    └─────────┬──────────┘
                              │ GET /health, /health/ready
                    ┌─────────▼──────────┐
                    │   Backend API      │   ASP.NET Core 10
                    │   :8080            │   Modular monolith
                    └─────────┬──────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
     ┌─────────────────┐            ┌──────────────────┐
     │   PostgreSQL    │            │      Redis       │
     │   :5432         │            │      :6379       │
     │   schema: quant │            │                  │
     └─────────────────┘            └──────────────────┘

     ┌─────────────────┐            ┌──────────────────┐
     │  Python quant   │            │   C++ engine     │
     │  packaging,     │            │   CMake, CTest,  │
     │  pytest, ruff,  │            │   GoogleTest     │
     │  mypy           │            │                  │
     └─────────────────┘            └──────────────────┘
        not yet wired                  not yet wired
        to the backend                 to the backend
```

The Python and C++ layers are real, built and tested, but they do not yet
communicate with the backend. Establishing the toolchains before there is
pressure to use them is the point of Phase 0.

## Technology stack

| Layer         | Choice                        | Why                                                                 |
| ------------- | ----------------------------- | ------------------------------------------------------------------- |
| Backend       | C# / ASP.NET Core 10 (LTS)    | Strong typing, first-class async, mature EF Core migrations          |
| Frontend      | React 19 + TypeScript, Vite   | Dense data UI; Vite because no server rendering is needed            |
| Database      | PostgreSQL 17                 | Correctness, window functions, and a credible time-series path       |
| Cache         | Redis 8                       | Sub-millisecond state, pub/sub for the eventual realtime path        |
| Quant         | Python 3.12+, pytest, ruff    | Where the numerical ecosystem lives                                  |
| Performance   | C++20, CMake, CTest           | For the components where GC pauses are the constraint                |
| Containers    | Docker Compose                | One command to a working environment                                 |
| CI            | GitHub Actions                | Builds and tests all four stacks                                     |

Decisions are recorded in [`decisions/`](decisions/).

## Architectural principle: modular monolith

One deployable backend, with modules separated by project boundaries rather
than by network boundaries. See
[ADR-001](decisions/ADR-001-modular-monolith.md).

```
backend/src/
├── PersonalQuant.Api/              HTTP, middleware, composition
├── PersonalQuant.Application/      use cases, abstractions
├── PersonalQuant.Domain/           the financial model
└── PersonalQuant.Infrastructure/   PostgreSQL, Redis, external providers
```

Dependencies point inward. `Domain` references nothing. `Application`
references `Domain`. `Infrastructure` implements what `Application` declares.
`Api` composes. The build enforces this: there is no project reference that
would allow `Domain` to reach for a database.

In Phase 0, `Domain` and `Application` are close to empty by design. The
financial model is Phase 1 work, and inventing it before there is a use case
would be guessing.

## Component responsibilities

| Component        | Owns                                                        | Phase 0 state                     |
| ---------------- | ----------------------------------------------------------- | --------------------------------- |
| `Api`            | Routing, CORS, exception handling, health, OpenAPI           | Health endpoints only             |
| `Application`    | Use cases, ports such as `IClock`                            | `IClock` and the DI seam          |
| `Domain`         | Instruments, prices, positions, orders, risk                 | Empty — Phase 1 onwards           |
| `Infrastructure` | EF Core context, migrations, Redis, health checks            | Connection + migration pipeline   |
| Frontend         | The terminal UI                                              | System status, capability map     |
| Quant            | Factors, strategies, backtests, analytics                    | Packaging and config reader       |
| C++ engine       | Latency-sensitive components                                 | Build, version reporting, tests   |

## Data flow today

There is exactly one flow, and it is a diagnostic one:

```
Browser
  │  GET /health              (liveness — touches nothing external)
  │  GET /health/ready        (readiness — probes both dependencies)
  ▼
Backend
  ├── PostgreSqlHealthCheck ──► NpgsqlDataSource ──► SELECT 1
  └── RedisHealthCheck ───────► multiplexer ──────► PING
  ▼
{ "status": "Healthy", "checks": [ { "name": "postgres", ... } ] }
  ▼
System status panel
```

Two properties of this flow are deliberate and are covered by tests:

1. **Liveness never touches a dependency.** A database outage must not cause a
   supervisor to restart a healthy API process.
2. **Readiness leaks nothing.** Failures are logged in full and reported to the
   caller as `"PostgreSQL is not reachable."` — no host, port, user, or driver
   message.

## Target architecture

The ten domains the system is intended to grow into. All PLANNED.

```
                    ┌────────────────────────────┐
                    │      TERMINAL (UI)         │
                    └─────────────┬──────────────┘
                                  ▼
                    ┌────────────────────────────┐
                    │      APPLICATION / API     │
                    └─────────────┬──────────────┘
             ┌────────────────────┼────────────────────┐
             ▼                    ▼                    ▼
      ┌─────────────┐     ┌──────────────┐     ┌──────────────┐
      │ MARKET DATA │     │  RESEARCH    │     │  INSTRUMENT  │
      │ price,trade │     │  fundamentals│     │  MASTER      │
      │ book, OHLCV │     │  filings,news│     │  identity    │
      └──────┬──────┘     └──────┬───────┘     └──────┬───────┘
             └────────────────┬──┴────────────────────┘
                              ▼
                 ┌────────────────────────┐
                 │ NORMALIZATION +        │
                 │ DATA QUALITY           │
                 └───────────┬────────────┘
                             ▼
                 ┌────────────────────────┐
                 │ STORAGE                │
                 │ PostgreSQL │ Redis     │
                 └───────────┬────────────┘
             ┌───────────────┼───────────────┐
             ▼               ▼               ▼
        Analytics      Quant Engine      Portfolio
             │               │               │
             ▼               ▼               ▼
         Signals        Backtesting        Risk
                             │               │
                             └───────┬───────┘
                                     ▼
                              Paper Trading
                                     ▼
                              OMS ──► Broker
                                     ▼
                            Execution + Fills
                                     ▼
                               Monitoring
                                     │
                                     └──► feeds research
```

### Delivery tiers

Implementation proceeds in vertical slices, not tier by tier. The tiers
describe what the system *becomes*, in order:

| Tier | Becomes            | Phases | Content                                                        |
| ---- | ------------------ | ------ | -------------------------------------------------------------- |
| 1    | Research terminal  | 1–6    | Instrument master, market data, quality, charts, news, screener |
| 2    | Quant platform     | 7–10   | Factors, backtesting, portfolio, risk                           |
| 3    | Trading system     | 11–14  | Paper trading, OMS, broker, reconciliation                      |
| 4    | Engineered system  | 15–18  | C++ hot path, AI analyst, hardening                             |

See [`../roadmap/phases.md`](../roadmap/phases.md).

## Constraints that shape the design

These are decided now because they are expensive to retrofit.

### Canonical instrument identity

`AAPL`, `AAPL.US`, `AAPL NASDAQ`, `US0378331005` and `BBG000B9XRY4` may all
denote one security. Every other domain joins on instrument identity, so the
instrument master is Phase 1 — before any price is stored. Provider symbols
become aliases of a canonical internal ID, never the key itself.

### Point-in-time correctness

A fact has two timestamps: the period it describes and the moment it became
knowable. Q4 revenue for a fiscal period ending in December is not knowable to
a strategy running in January if it was reported in February.

Storing only the fiscal period makes look-ahead bias unavoidable and, worse,
invisible — backtests simply look better than reality. Fundamental and
corporate-action data therefore carries both timestamps from Phase 5, and the
backtester filters on knowability, not on period.

### Data quality is a stage, not a hope

Provider data is wrong sometimes: out-of-order sequence numbers, duplicate
trade IDs, a price off by a factor of ten. Validation sits between ingestion
and storage so bad ticks never reach a backtest or a risk calculation
(Phase 3).

### Deterministic execution path

The path from decision to order is deterministic and ordered:

```
strategy → signal → portfolio construction → risk → OMS → execution → broker
```

A strategy never calls a broker. The risk engine sits in front of the OMS and
can reject, so position limits are enforced structurally rather than by
convention.

### AI advises; it never trades

The AI analyst (Phase 16) reads market data, filings, news and fundamentals and
produces cited analysis and hypotheses. It has no path to the OMS. Order
sizing, risk approval and execution stay deterministic and reviewable. See
[ADR-007](decisions/ADR-007-private-proprietary-repository.md) for the
repository posture and the roadmap for where this boundary is enforced.

`LIVE_TRADING_ENABLED` defaults to `false` and stays false until Phase 13.

## What Phase 0 deliberately does not do

- No financial entities, tables, or migrations beyond an empty baseline.
- No market data, provider clients, or API keys in use.
- No authentication — there is nothing yet to protect, and guessing the model
  now would be rework.
- No message broker, Kubernetes, or service mesh. See
  [ADR-001](decisions/ADR-001-modular-monolith.md).
- No microservices. One deployable, four projects.
