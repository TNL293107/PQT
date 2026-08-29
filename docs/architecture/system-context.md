# System Context

Who and what the terminal interacts with, and which of those interactions are
real today.

Target market is Vietnam — HOSE, HNX, UPCOM — per
[ADR-008](decisions/ADR-008-vietnam-market-first.md).

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

The single human user. Runs research, reviews results, and (from Phase 15) is
the only party that can enable live trading. There is no multi-user model and
no authentication, because there is exactly one operator and nothing yet worth
protecting. Authentication arrives with Phase 19 — Production Hardening, or
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
structure. Seven migrations so far: the baseline schema, the instrument master,
its search index, the classification taxonomy, market data ingestion with its
provenance and resume state, identifier aliases, and the data-quality tables.

### Redis — IMPLEMENTED

Version 8, running in Docker with append-only persistence. The backend
connects lazily and reports availability through readiness. It caches nothing
yet. Intended eventually for quote caching, realtime state, pub/sub fan-out and
rate limiting.

### Python quant layer — PLANNED (link), IMPLEMENTED (toolchain)

Installable package with pytest, ruff and mypy configured and passing. It reads
the same `POSTGRES_*` configuration the backend uses, but nothing invokes it
from the backend and it opens no connection. How the two layers exchange work —
shared database, a job queue, or a local service — is decided in
[ADR-016](decisions/ADR-016-python-dotnet-research-boundary.md), delivered by
U10 of the Research Foundation Upgrade.

### C++ engine — PLANNED (link), IMPLEMENTED (toolchain)

CMake project building a static library, a CLI and a GoogleTest suite driven by
CTest. Not referenced by the backend. Phase 17 decides the interop mechanism
(P/Invoke, a native library, or a separate process).

## External systems — all PLANNED

### Market data providers — IMPLEMENTED

Historical bars for HOSE, HNX and UPCOM; realtime quotes and trades later.

The only source shipped is file-backed — CSV exports read from disk, which is a
real provider under the same contract, and how most Vietnamese historical data
actually changes hands. It exists so the pipeline can be demonstrated without a
licence. A vendor client implements the same interface and inherits the
validation, deduplication, retry, spacing, audit and checkpointing without being
able to opt out of any of them.

Providers are reached through an `IMarketDataProvider` abstraction so no single
vendor is hard-coded — Vietnamese market data providers are fewer and less
mature than their US counterparts, and switching is a realistic prospect.

Before any provider is selected it must be evaluated against
[`data-policy.md`](data-policy.md): API terms, redistribution rights, storage
rights, and whether personal or commercial use is permitted. Rate limits and
correction/restatement handling matter as much as coverage — a feed that
silently restates history breaks reproducible backtests.

### Corporate action sources

Dividends, splits, rights issues, bonus shares and symbol changes, from
Phase 4. Rights issues and bonus shares are routine on Vietnamese exchanges
rather than occasional, so a provider that treats them as an afterthought is
not usable.

Actions are versioned and applied as an adjustment layer over retained raw
prices, never as an overwrite.

### Fundamental data and filings

Financial statements and regulatory filings, from Phase 6. The binding
requirement is point-in-time correctness: each fact must carry both the fiscal
period it describes and the moment it became public. A provider that supplies
only the fiscal period cannot support honest backtesting.

### News providers

Articles and market commentary, from Phase 11. Requires entity extraction and
mapping to canonical instrument IDs. Redistribution of article text is almost
never granted — expect to store references and metadata rather than bodies.

Alternative data enters here too: search trends, news volume, social
sentiment, and the foreign and proprietary flow series that Vietnamese market
participants watch closely.

### External data ecosystems — DEFERRED

Tier 2 sources — non-Vietnamese equities, ETFs, options, FX, crypto, global
macro and news — are reached, if ever, through a collector the **operator**
installs and runs outside this system. **OpenBB is one such option and is
`DEFERRED`**: no PQT code contacts it, imports it, depends on it or ships it,
and none is planned. Its output would enter the same way any file export does,
through the file-ingestion seam described above.

The reasoning — an `AGPL-3.0-only` project against this repository's
proprietary licence, no Vietnamese equity provider identified in the reviewed
catalogue, and no point-in-time model — is in
[ADR-019](decisions/ADR-019-openbb-boundary.md), with the research record in
[`openbb-evaluation.md`](openbb-evaluation.md).

One consequence is recorded there and repeated here because it is a lineage
question: the file seam stamps every bar `SourceCode = FILE`, so an external
collector's true origin is not preserved at bar level. Answering that is a
prerequisite for any sustained Tier 2 use.

### Broker — PLANNED, Phase 15

Order placement, fills, and position reporting, reached through an adapter
interface so the system is not welded to one venue.

Two constraints are fixed now:

- The risk engine sits **in front of** the OMS. There is no code path from a
  strategy directly to a broker.
- `LIVE_TRADING_ENABLED` defaults to `false` and remains false through
  Phase 14. Paper trading (Phase 13) comes first, and reconciliation
  (Phase 16) follows immediately.

### AI provider — PLANNED, Phase 18

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
| External ecosystems     | None contacted. Tier 2 collectors are operator-run, out-of-process, and reach PQT only as files ([ADR-019](decisions/ADR-019-openbb-boundary.md)) |
| Repository → the world  | Private, proprietary; secrets git-ignored           |

Health endpoints are anonymous by design and are written to disclose nothing
beyond up/down per dependency. That property is enforced by an integration
test, not by convention.
