# System Context

Who and what the terminal interacts with, and which of those interactions are
real today.

Every element is labelled:

- **IMPLEMENTED** — exists, builds, and is covered by tests.
- **PLANNED** — an architectural intention. No code, no credentials, no
  contract with any provider.

## Context diagram

```
                          ┌──────────────────────┐
                          │        Operator      │   IMPLEMENTED
                          │   (single human user)│
                          └───────────┬──────────┘
                                      │ browser
                                      ▼
                          ┌──────────────────────┐
                          │   Terminal frontend  │   IMPLEMENTED
                          │   React + TypeScript │
                          └───────────┬──────────┘
                                      │ HTTPS/JSON
                                      ▼
   ┌──────────────────────────────────────────────────────────────┐
   │                     Backend API (ASP.NET Core)               │   IMPLEMENTED
   └───┬───────────────┬──────────────┬────────────┬──────────────┘
       │               │              │            │
       ▼               ▼              ▼            ▼
 ┌───────────┐   ┌───────────┐  ┌───────────┐  ┌──────────────┐
 │PostgreSQL │   │  Redis    │  │  Python   │  │  C++ engine  │
 │IMPLEMENTED│   │IMPLEMENTED│  │  PLANNED  │  │   PLANNED    │
 │           │   │           │  │  (link)   │  │   (link)     │
 └───────────┘   └───────────┘  └───────────┘  └──────────────┘

       ┌───────────────────────────────────────────────────┐
       │              External systems — all PLANNED       │
       ├───────────────────┬───────────────────────────────┤
       │ Market data       │ prices, trades, order book    │
       │ Fundamentals      │ statements, filings           │
       │ News              │ articles, corporate actions   │
       │ Broker            │ orders, fills, positions      │
       │ AI provider       │ analysis and summarisation    │
       └───────────────────┴───────────────────────────────┘
```

Nothing in the external systems box is contacted by any code in this
repository. No provider account is required to build, test, or run the
environment.

## Actors

### Operator — IMPLEMENTED

The single human user. Runs research, reviews results, and (from Phase 13) is
the only party that can enable live trading. There is no multi-user model and
no authentication, because there is exactly one operator and nothing yet worth
protecting. Authentication arrives with Phase 17 — Production Hardening, or
sooner if anything leaves localhost.

## Internal systems

### Terminal frontend — IMPLEMENTED

React 19 + TypeScript, built by Vite, served by nginx. Today it renders the
system status panel and the capability map. It talks to exactly two endpoints,
`GET /health` and `GET /health/ready`.

### Backend API — IMPLEMENTED

ASP.NET Core 10 modular monolith. Owns configuration, logging, exception
handling, health checks and OpenAPI. Serves only diagnostics endpoints.

### PostgreSQL — IMPLEMENTED

System of record. Version 17, running in Docker with a persistent named
volume. The application owns the `quant` schema; EF Core migrations own its
structure. Phase 0 applies one baseline migration that creates the schema and
the migrations history table and nothing else.

### Redis — IMPLEMENTED

Version 8, running in Docker with append-only persistence. The backend
connects lazily and reports availability through readiness. It caches nothing
yet. Intended eventually for quote caching, realtime state, pub/sub fan-out and
rate limiting.

### Python quant layer — PLANNED (link), IMPLEMENTED (toolchain)

Installable package with pytest, ruff and mypy configured and passing. It reads
the same `POSTGRES_*` configuration the backend uses, but nothing invokes it
from the backend and it opens no connection. How the two layers exchange work —
shared database, a job queue, or a local service — is a Phase 7 decision, taken
when there is a workload to size it against.

### C++ engine — PLANNED (link), IMPLEMENTED (toolchain)

CMake project building a static library, a CLI and a GoogleTest suite driven by
CTest. Not referenced by the backend. Phase 15 decides the interop mechanism
(P/Invoke, a native library, or a separate process).

## External systems — all PLANNED

### Market data providers

Historical bars and, later, realtime quotes and trades. Entry point is Phase 2.

Before any provider is selected it must be evaluated against
[`data-policy.md`](data-policy.md): API terms, redistribution rights, storage
rights, and whether personal or commercial use is permitted. Rate limits and
correction/restatement handling matter as much as coverage — a feed that
silently restates history breaks reproducible backtests.

### Fundamental data and filings

Financial statements and regulatory filings, from Phase 5. The binding
requirement is point-in-time correctness: each fact must carry both the fiscal
period it describes and the moment it became public. A provider that supplies
only the fiscal period cannot support honest backtesting.

### News providers

Articles and corporate actions, from Phase 5. Requires entity extraction and
mapping to canonical instrument IDs. Redistribution of article text is almost
never granted — expect to store references and metadata rather than bodies.

### Broker — PLANNED, Phase 13

Order placement, fills, and position reporting, reached through an adapter
interface so the system is not welded to one venue.

Two constraints are fixed now:

- The risk engine sits **in front of** the OMS. There is no code path from a
  strategy directly to a broker.
- `LIVE_TRADING_ENABLED` defaults to `false` and remains false through
  Phase 12. Paper trading (Phase 11) comes first, and reconciliation
  (Phase 14) follows immediately.

### AI provider — PLANNED, Phase 16

Summarisation and analysis over data the system already holds, with citations
back to the underlying records.

The boundary is architectural, not a guideline: **the AI analyst has no path to
the OMS.** It may read, explain, and propose hypotheses. Order sizing, risk
approval and execution remain deterministic and reviewable.

## Trust boundaries

| Boundary                | Today                                              |
| ----------------------- | -------------------------------------------------- |
| Browser → API           | CORS restricted to configured origins; no auth yet  |
| API → PostgreSQL        | Credentials from environment; parameterised access  |
| API → Redis             | In-network; password supported, unset locally       |
| API → external          | None exist                                          |
| Repository → the world  | Private, proprietary; secrets git-ignored           |

Health endpoints are anonymous by design and are written to disclose nothing
beyond up/down per dependency. That property is enforced by an integration
test, not by convention.
