# Personal Quantitative Trading & Market Intelligence Terminal

A Bloomberg-inspired quantitative research and trading workstation for the
**Vietnam market** (HOSE, HNX, UPCOM), built as a long-term personal
engineering project.

[![CI](https://github.com/TNL293107/PQT/actions/workflows/ci.yml/badge.svg)](https://github.com/TNL293107/PQT/actions/workflows/ci.yml)

---

## Current status

```
Phase 0–4                     COMPLETE *
Research Foundation Upgrade   IN PROGRESS   ← current work
Phase 5–20                    PLANNED

* implemented and tested, but empirically unvalidated against
  real Vietnamese market data until U3 is completed.
```

|              |                                                              |
| ------------ | ------------------------------------------------------------ |
| **Complete** | Phases 0–4 — master, ingestion, quality, adjusted prices      |
| **Current**  | Research Foundation Upgrade — U1–U10 (phases run 0–20)        |
| **Next**     | Phase 5 — Market Intelligence Terminal, after **Gate A**      |
| **Runs**     | `docker compose up --build` — four services, health-gated     |
| **Tests**    | 732 green in CI — 539 unit, 102 integration, 71 Vitest, 14 pytest, 6 CTest |
| **Licence**  | Proprietary. Public to read, not to reuse.                    |

**The caveat that matters.** Phases 2–4 are built, tested and reviewed. They
have never processed a real Vietnamese price — the only market data here is a
six-session synthetic series for a ticker listed on no venue. That makes their
correctness properties **designed and tested but not empirically validated**:

```
Implemented + tested   ≠   Empirically validated with real market data
```

Closing that gap is **U3**, and it is mandatory. See the
[canonical roadmap](docs/roadmap/pqt-roadmap-v2.md).

**What exists.** Liveness and readiness endpoints that probe PostgreSQL and
Redis for real; a complete instrument master — identity and listing lifecycle,
a two-level sector taxonomy, identifier aliases, and a provider import pipeline
that makes every vendor's spelling of a security reach one canonical ID;
search, symbol resolution, paging and a read-only instrument API; a market data
ingestion pipeline with validation, deduplication, retained raw payloads,
resume checkpoints and an audit record for every attempt including the ones
that did nothing; data-quality rules that measure a series against the venue's
price band and its trading calendar, and record what they find rather than
correcting it; corporate actions with adjusted prices applied as a factor on
read, so the raw series is never rewritten; a terminal with Ctrl+K security
search and a current-security context. All of it covered by tests that run
against real containers, not mocks.

**What does not exist.** Point-in-time reads, universe membership, real market
data, fundamentals, news, screening, factors, backtesting, portfolio, risk,
orders, broker integration, AI. The `quant/` and `cpp-engine/` layers contain
packaging, tooling and tests but **no financial code**. There is also no write
surface over HTTP at all: reference data arrives through the import pipelines
and bars through ingestion, both driven by the host on a schedule the operator
configures, and a trigger endpoint waits for authentication. None of these are
stubbed or half-built; they are simply not written.

## Current work — Research Foundation Upgrade

Ten workstreams between Phase 4 and Phase 5. Purpose: make the Phase 0–4 data
architecture **empirically valid**, and establish the research foundation the
quantitative phases depend on.

| | Workstream | Gate |
| --- | --- | --- |
| **U1** | Temporal / point-in-time correctness | A |
| **U2** | Universe & survivorship correctness | A |
| **U3** | **Real Vietnamese data provider integration — MANDATORY** | A |
| **U4** | Adjustment & announcement awareness | A |
| **U5** | Canonical dataset contract | A |
| **U6** | Quant research abstraction layer | B |
| **U7** | Research storage | B |
| **U8** | Qlib research adapter | B |
| **U9** | Experiment tracking & reproducibility | B |
| **U10** | Python / .NET research boundary | B |

**Gate A** — U1–U5 plus real-data validation — gates Phase 5.
**Gate B** — U6–U10 — completes the Upgrade and runs in parallel with Phase 5.

> **U3 is mandatory. It must not be downgraded, deferred, or satisfied by
> fixtures.** Synthetic data does not satisfy Gate A.

The [canonical roadmap](docs/roadmap/pqt-roadmap-v2.md) sets out all twenty-one
phases, both gates, and what each one has to deliver.

---

## Goals

Build the closed loop that separates a research platform from a chart viewer:

```
market data → research → signal → backtest → risk → execution → monitoring
     ▲                                                              │
     └──────────────────── analysis feeds the next hypothesis ──────┘
```

The system is data-first. The terminal UI is a view onto the domain, not the
place behaviour lives — a chart on top of a model that cannot tell one
provider's spelling of `FPT` from another's is worth nothing.

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
│   │   ├── PersonalQuant.Domain/           financial model
│   │   └── PersonalQuant.Infrastructure/   PostgreSQL, Redis, migrations
│   └── tests/               unit + integration
├── frontend/                React terminal
├── quant/                   Python research layer
├── cpp-engine/              C++ performance layer
├── data/                    schemas and fixtures (bulk data git-ignored)
├── docs/
│   ├── architecture/        overview, context, data policy, ADRs
│   ├── development/         local setup, git workflow
│   └── roadmap/             canonical roadmap (v2)
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
- **Instrument master (Phase 1, workstream 1)** — `Exchange` and `Instrument`
  aggregates with strongly-typed identity, value objects instead of bare
  strings, and a listing lifecycle that rejects every illegal transition.
  Ticker uniqueness is enforced per exchange over active instruments only, so
  a ticker released on delisting can be reissued without destroying the
  previous holder's history. Instruments are never deleted. See
  [ADR-009](docs/architecture/decisions/ADR-009-instrument-identity-and-ticker-lifecycle.md).
- **Sector and industry (Phase 1, workstream 2)** — a two-level taxonomy an
  instrument points into. It points at an industry and reaches its sector
  through it, so the two levels cannot disagree, and the link is nullable
  because an index is in no industry and an unmapped security is not the same
  thing as an unclassified one.
- **Identifier aliases and provider import (Phase 1, workstreams 3–5)** — the
  part that makes the master's promise true. An ISIN and a FIGI are validated
  by check digit and unique across the whole master; a provider symbol is
  unique only within the provider that issued it, and both are enforced by
  partial unique indexes rather than by convention. Import normalises a
  vendor's spelling — `FPT`, `FPT.HM`, `FPT:VN`, `HOSE:FPT` all resolve to one
  security — then deduplicates strongest-signal-first, records the spelling as
  an alias, and rejects rather than resolves a row whose identifiers and symbol
  disagree.
- **Instrument search (Phase 1, workstreams 6–7)** —
  `GET /instruments`, `GET /instruments/search?q=`,
  `GET /instruments/resolve?symbol=`, `GET /instruments/{id}` and
  `GET /instruments/{id}/related`. Matching folds Vietnamese diacritics and case, so
  `ngan hang` finds `Ngân hàng`. Ranking is deterministic and evaluated in the
  database: exact ticker, ticker prefix, exact name, name prefix, name
  contains. Symbol resolution reports ambiguity rather than guessing when a
  ticker is live on two venues. See
  [ADR-010](docs/architecture/decisions/ADR-010-instrument-search-and-security-context.md).
- **Market data ingestion (Phase 2)** — fetch, validate, normalise,
  deduplicate, persist, audit. A bar's identity is its instrument, resolution
  and opening instant, so deduplication is the primary key rather than a rule
  in the writer; prices are `numeric(18,6)` because a close that comes back a
  fraction different compounds into returns the market never produced. No
  provider is hard-coded — a CSV file source is the reference implementation,
  so the pipeline runs on a fresh clone without a licence. Raw payloads are
  retained so re-normalising is always possible, every attempt is audited
  including the skipped ones, and a checkpoint advances to the newest bar
  actually stored rather than to the end of the range that was asked for. See
  [ADR-011](docs/architecture/decisions/ADR-011-market-data-ingestion.md).
- **Data quality (Phase 3)** — the checks a single bar cannot answer. A
  session-to-session move is measured against the venue's own daily band —
  HOSE ±7%, HNX ±10%, UPCOM ±15% — because the exchange rejects orders outside
  it, so a larger move did not happen as printed. Missing and unexpected
  sessions are measured against the trading calendar. Findings are recorded and
  stay open until something accounts for them; nothing is corrected, and the
  bar is always kept. Every bar carries which rules produced it and which have
  checked it, so changing a rule is a query rather than a re-validation of
  everything. See
  [ADR-013](docs/architecture/decisions/ADR-013-data-quality-and-lineage.md).
- **Corporate actions and adjusted prices (Phase 4)** — eight action types,
  with rights issues and bonus shares first-class rather than dividend
  variants, because in this market they are routine and their maths is not a
  dividend's. An adjustment is a price multiplier and a share multiplier stored
  beside the bars, computed against the close of the last session before the
  ex-date and stamped with the action version it came from, so a restated ratio
  makes its factor stale by comparison rather than forcing a re-derivation of
  everything. **Raw bars are never rewritten** —
  `GET /instruments/{id}/bars` returns an adjusted series by default and says
  which it returned, and unadjusted is available labelled, alongside
  `GET /instruments/{id}/corporate-actions`. An action with no price before it
  is rejected rather than guessed. An action whose ex-date lands on an open
  price-limit finding closes it, which is what Phase 3 left this phase to do.
  See
  [ADR-014](docs/architecture/decisions/ADR-014-corporate-actions-and-adjusted-prices.md).
- **Terminal security search** — `Ctrl+K`, type, arrow, `Enter`. Sets the
  terminal's current security, which every later module reads by canonical
  identifier rather than by ticker.
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
than passing quietly. CI runners have a daemon, so all 102 of them execute
there — a green build means the search ranking, the partial unique index, the
point-in-time observation window and the migration were exercised against a
real PostgreSQL 17, not a substitute.

Those 102 share one database and nothing resets it between tests, so each one
isolates itself by using data no other test claims — its own exchange code,
ticker and identifier. A test that reuses one collides on a unique index and
fails in its setup.

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
- `LIVE_TRADING_ENABLED` defaults to `false` and stays false until Phase 15.

**There is no authentication yet.** There is nothing to protect, and guessing
the model now would be rework. It arrives in Phase 19, or sooner if any part of
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

Twenty-one phases, numbered 0–20. **Phase numbering is canonical and frozen** —
no phase is ever renumbered, inserted or merged. Phases 0 through 4 are
complete, the Research Foundation Upgrade is under way, and everything else is
planned.

| Stage | Becomes | Phases | Status |
| --- | --- | --- | --- |
| 1 | Data foundation | 0–4 | COMPLETE * |
| — | **Research foundation** | **U1–U10** | **IN PROGRESS** |
| 2 | Quant platform | 5–12 | PLANNED |
| 3 | Trading system | 13–16 | PLANNED |
| 4 | Engineered system | 17–20 | PLANNED |

Five phases carry the dependency chain everything else inherits, and none may
be done superficially: **2** (market data ingestion) → **3** (data quality) →
**4** (corporate actions) → **8** (backtesting) → **9** (risk).

> Data correct → research trustworthy → backtest trustworthy → risk
> trustworthy → execution trustworthy.
>
> Data wrong → all of it wrong, quietly.

**Phase 1 — Instrument Master** is complete. Searching `FPT` resolves to
exactly one security, that security is classified into a sector, and every
provider's spelling of it — `FPT.HM` from one vendor, `FPT:VN` from another —
maps to the same canonical ID. Importing the same symbol list twice creates
nothing the second time.

**Phase 2 — Market Data Ingestion** is complete. Bars are stored against the
canonical identifier, deduplicated by the schema rather than by convention, and
every ingestion attempt leaves a record explaining what it did — which is what
makes a gap in a series answerable instead of merely visible. What it does not
have is a scheduler; runs are driven by the host until there is authentication
to put in front of a trigger.

**Phase 3 — Data Normalization & Quality** is complete. A discontinuity larger
than the venue permits, a session the calendar expected and did not get, and a
bar on a day the market was shut are each recorded as a finding that stays open
until something explains it. Nothing is corrected automatically. The honest
caveat is the calendar — it has to be imported, because Vietnam's cannot be
derived, and until one is, completeness is reported as unmeasured rather than
guessed.

**Phase 4 — Corporate Actions & Adjusted Data** is complete, which closes the
data foundation. A split no longer reads as a 50% crash: the series is returned
adjusted by default, the raw bars are untouched, and the adjustment is a stored
factor that can be corrected when a ratio is restated. An action landing on an
open price-limit finding closes it, so the queue Phase 3 left behind is drained
by the phase that can actually answer it. The caveat here is the mirror of the
calendar's: nothing cross-checks an imported ratio against the price series, so
a ratio transcribed as 20 instead of 2 produces a plausible factor and a ruined
chart.

## Long-term advanced research

Placed, not designed. Each has exactly one canonical owner, and none is built
during the Research Foundation Upgrade — the Upgrade only establishes their
prerequisites.

| Capability | Owner | Status |
| --- | --- | --- |
| Prediction Market Mispricing | Phase 12 | `RESEARCH ONLY` — no legal, liquid Vietnamese market exists |
| Information Diffusion | Phase 12 | `PLANNED` — unblocked by U1's announcement time |
| Implied Risk-Neutral Distribution (Breeden–Litzenberger) | Phase 12 | `BLOCKED` — Vietnam has no listed equity options |
| Backtest Overfitting Detection — core | Phase 8 | `PLANNED` — needs U9's trial count |
| Advanced multiple-testing (Reality Check, SPA) | Phase 12 | `PLANNED` |

Rationale in [docs/architecture/advanced-research.md](docs/architecture/advanced-research.md).

## How to read status labels

| Label | Means |
| --- | --- |
| `COMPLETE` | Implemented, tested, and in the repository |
| `IN PROGRESS` | Being built now |
| `PLANNED` | Decided and scheduled; no code |
| `BLOCKED` | Cannot proceed — a prerequisite outside the project is missing |
| `DEFERRED` | Deliberately postponed; the trigger to revisit is recorded |
| `RESEARCH ONLY` | Explored in research; never in the production runtime |

Architecture documents additionally mark sections **DESIGNED** where a design
exists but no code does. These six are the only status values any PQT document
uses.

Full detail in [docs/roadmap/pqt-roadmap-v2.md](docs/roadmap/pqt-roadmap-v2.md).

## Design constraints decided up front

Recorded now because they are expensive to retrofit:

- **Canonical instrument identity.** Provider symbols are aliases, never keys.
  In Vietnam this is forced rather than merely wise — tickers change on
  exchange transfer and are reassigned after delisting.
- **Point-in-time correctness.** Event time, effective time, announcement time,
  observation time and revision are five distinct things and are never
  collapsed. *Designed, not yet implemented for prices* — `quant.bars` is
  overwritten on restatement today, and U1 adds the observation history and the
  `knownAsOf` read. See
  [data-architecture.md](docs/architecture/data-architecture.md).
- **No survivorship bias.** Historical universes must reflect what actually
  existed then. *Designed, not yet implemented* — U2.
- **Data quality is a pipeline stage**, not an assumption. Thresholds are
  per-exchange, matching the ±7% / ±10% / ±15% daily price limits.
- **Raw data is never overwritten.** Corporate actions are applied as a
  versioned adjustment layer over retained raw prices.
- **Deterministic execution path.** `strategy → signal → portfolio → risk → OMS
  → execution → broker`. A strategy has no route to a broker; the risk engine
  can reject.
- **AI advises, never trades.** The Phase 18 analyst has no path to the OMS.
  It reaches data through a registry of declared tools, never arbitrary queries.
- **External data ecosystems are optional and out-of-process.** Vietnamese data
  is PQT's own; anything else is reached, if ever, through a collector the
  operator runs outside the system, and never becomes canonical without passing
  the same normalisation, validation and provenance stages. See
  [ADR-019](docs/architecture/decisions/ADR-019-openbb-boundary.md).

## Documentation

| Document                                                                       | Contents                        |
| ------------------------------------------------------------------------------ | ------------------------------- |
| [Roadmap v2.0](docs/roadmap/pqt-roadmap-v2.md)                                 | **Canonical** — phases 0–20, U1–U10, gates, statuses |
| [Architecture overview](docs/architecture/overview.md)                         | Current and target architecture |
| [Data architecture](docs/architecture/data-architecture.md)                    | Temporal model, universes, adjustment, dataset contract, storage |
| [Quant research architecture](docs/architecture/quant-research-architecture.md) | Research protocols, Python/.NET boundary, experiments |
| [Qlib integration](docs/architecture/qlib-integration.md)                      | Adapter boundary and removal procedure |
| [OpenBB evaluation](docs/architecture/openbb-evaluation.md)                    | What OpenBB is, what PQT takes from it, source tiers |
| [Advanced research](docs/architecture/advanced-research.md)                    | The five long-term research capabilities |
| [System context](docs/architecture/system-context.md)                          | Actors and external systems     |
| [Data policy](docs/architecture/data-policy.md)                                | Market data licensing, source tiers |
| [Instrument search](docs/architecture/instrument-search.md)                    | Search, resolution, current security |
| [ADRs](docs/architecture/decisions/)                                           | Nineteen recorded decisions     |
| [Local setup](docs/development/local-setup.md)                                 | Build, run, test, troubleshoot  |
| [Git workflow](docs/development/git-workflow.md)                               | Branching and commit standards  |

## License

**Proprietary — all rights reserved.** This repository is public to be read,
but it is **not** an open-source project and no rights are granted to reuse
the code. See [LICENSE.md](LICENSE.md).

Third-party dependencies remain under their own licences. Market data is
governed by provider terms, not by this licence.

Nothing produced by this software is financial advice.
