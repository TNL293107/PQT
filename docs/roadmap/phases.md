# Roadmap

Implementation proceeds in vertical slices. Each phase delivers something
demonstrable end to end rather than a horizontal layer that cannot be used
until the next one lands.

Status values: **COMPLETE** · **IN PROGRESS** · **PLANNED**

| Phase | Name                       | Tier | Status   |
| ----- | -------------------------- | ---- | -------- |
| 0     | Foundation & Architecture  | —    | COMPLETE |
| 1     | Instrument Master          | 1    | PLANNED  |
| 2     | Market Data                | 1    | PLANNED  |
| 3     | Data Quality               | 1    | PLANNED  |
| 4     | Research Terminal          | 1    | PLANNED  |
| 5     | Fundamentals & News        | 1    | PLANNED  |
| 6     | Screener & Research        | 1    | PLANNED  |
| 7     | Quant Research             | 2    | PLANNED  |
| 8     | Backtesting                | 2    | PLANNED  |
| 9     | Portfolio                  | 2    | PLANNED  |
| 10    | Risk                       | 2    | PLANNED  |
| 11    | Paper Trading              | 3    | PLANNED  |
| 12    | OMS & Execution            | 3    | PLANNED  |
| 13    | Broker Integration         | 3    | PLANNED  |
| 14    | Reconciliation             | 3    | PLANNED  |
| 15    | C++ Performance            | 4    | PLANNED  |
| 16    | AI Analyst                 | 4    | PLANNED  |
| 17    | Production Hardening       | 4    | PLANNED  |
| 18    | Portfolio Release          | 4    | PLANNED  |

---

## Phase 0 — Foundation & Architecture · COMPLETE

Build a repository that another developer can clone, configure, run and
understand, with no business functionality in it.

**Delivered**

- Modular monolith backend: `Api`, `Application`, `Domain`, `Infrastructure`.
- `GET /health` (liveness) and `GET /health/ready` (readiness over PostgreSQL
  and Redis).
- EF Core + Npgsql with a baseline migration creating the `quant` schema.
- Redis connection with lazy connect and health reporting.
- React + TypeScript terminal with a live system status page.
- Python package with pytest, ruff and strict mypy.
- C++20 engine with CMake, GoogleTest and CTest.
- Docker Compose environment for all four services.
- GitHub Actions CI across all four stacks.
- Architecture documentation, seven ADRs, and this roadmap.

**Explicitly not delivered:** any financial entity, endpoint, or dataset.

---

## Tier 1 — Research Terminal

### Phase 1 — Instrument Master · PLANNED

Know what an instrument *is* before storing anything about it.

Entities: `Instrument`, `Exchange`, `AssetClass`, `Sector`, `Industry`,
`Currency`, `Identifier`.

```
Provider → import → normalize symbol → deduplicate → Instrument Master
```

Endpoints: `GET /instruments`, `GET /instruments/{id}`,
`GET /instruments/search?q=`, `GET /instruments/{id}/related`.

Done when searching `NVDA` resolves to exactly one security, and `AAPL`,
`AAPL.US`, `US0378331005` and `BBG000B9XRY4` all resolve to the same canonical
ID.

The canonical ID is internal. Provider and market identifiers are stored as
aliases — partly for correctness, partly because CUSIP and ISIN carry licensing
restrictions (see [data policy](../architecture/data-policy.md)).

### Phase 2 — Market Data · PLANNED

Historical OHLCV first, then realtime. Provider adapters normalise into a
canonical event shape keyed by canonical instrument ID.

Realtime introduces sequence numbers and gap detection: receiving
`1001, 1002, 1004, 1003` must be recognised as a problem, not stored as fact.

### Phase 3 — Data Quality · PLANNED

Validation between ingestion and storage: timestamp sanity
(`event_time <= receive_time`), positive prices, sequence continuity,
duplicate trade IDs, and outlier detection.

A price of `1812.00` in a series around `181.20` must never reach a backtest, a
risk calculation, or a portfolio valuation.

### Phase 4 — Research Terminal · PLANNED

The UI becomes useful: search, watchlists, charts, and a per-instrument
workspace. First phase where the frontend does substantial work.

### Phase 5 — Fundamentals & News · PLANNED

Financial statements stored as facts, not flat columns:

```
company · fiscal_period · concept · value · unit · source · filing_id · reported_at
```

`reported_at` is the load-bearing field. Without the separation of *fiscal
period* from *publication time*, look-ahead bias is unavoidable and invisible.

News requires deduplication, entity extraction and mapping to canonical
instrument IDs.

### Phase 6 — Screener & Research · PLANNED

Query the universe by fundamental and technical criteria, with ranking. The
point at which the terminal becomes a research platform rather than a chart
viewer.

---

## Tier 2 — Quant Platform

### Phase 7 — Quant Research · PLANNED

The Python layer starts doing real work: factor definitions, feature
engineering, cross-sectional analysis. Resolves the open question in
[ADR-004](../architecture/decisions/ADR-004-python-quant-layer.md) — how the
backend and quant layer exchange work.

### Phase 8 — Backtesting · PLANNED

Event-driven simulation over historical data, modelling cash, positions,
orders, fills, fees, slippage and corporate actions.

The correctness requirement is point-in-time discipline: the simulator may only
use information knowable at the simulated moment. A backtest that looks
unusually good is assumed to be leaking until proven otherwise.

### Phase 9 — Portfolio · PLANNED

Cash, positions, average cost, realised and unrealised P&L, exposure and
leverage. Shared by backtests and live tracking so both are measured
identically.

### Phase 10 — Risk · PLANNED

Pre-trade limits: max position, sector exposure, leverage, daily loss,
drawdown, order size, concentration.

The risk engine answers one question — *may this order proceed?* — and it must
be able to answer no.

---

## Tier 3 — Trading System

### Phase 11 — Paper Trading · PLANNED

A simulated venue behind the same interface a broker will implement, modelling
latency, partial fills, slippage, fees and rejections.

### Phase 12 — OMS & Execution · PLANNED

Order lifecycle as an explicit state machine:

```
CREATED → SUBMITTED → ACKNOWLEDGED → PARTIALLY_FILLED → FILLED
                   ↘ REJECTED
                   ↘ CANCEL_REQUESTED → CANCELLED
```

The path is fixed and ordered:

```
strategy → signal → portfolio construction → risk → OMS → execution → broker
```

A strategy has no route to a broker. Order events are written to PostgreSQL,
not only published to Redis — this is the durability requirement anticipated in
[ADR-003](../architecture/decisions/ADR-003-redis.md).

Execution algorithms (TWAP, VWAP, POV, iceberg) sit between the OMS and the
venue.

### Phase 13 — Broker Integration · PLANNED

A broker adapter interface with at least one implementation, so the system is
not welded to a single venue.

**This is the first phase in which `LIVE_TRADING_ENABLED` may be set to
`true`,** and only after Phase 11 and Phase 14 are both complete and proven.

### Phase 14 — Reconciliation · PLANNED

Compare internal state against broker state and alert on divergence.

If the system believes it holds 100 shares and the broker reports 90, something
is wrong — a missed fill, a duplicate event, a crash mid-write. A trading
system that cannot detect this is not trustworthy, regardless of how good its
backtests look.

---

## Tier 4 — Advanced Engineering

### Phase 15 — C++ Performance · PLANNED

Move measured bottlenecks to the C++ engine. Candidates: order book, market
data decoding, event bus.

Bound by
[ADR-005](../architecture/decisions/ADR-005-cpp-performance-layer.md): profile
first, benchmark the rewrite against what it replaces, and keep it in .NET if
the numbers do not justify the move.

### Phase 16 — AI Analyst · PLANNED

Retrieval-grounded analysis over data the system already holds, with citations.

```
question → query planner → market data + news + filings + fundamentals → LLM → cited analysis
```

**Architectural boundary: the AI analyst has no path to the OMS.** It may
analyse, explain and propose hypotheses. Order sizing, risk approval and
execution stay deterministic and reviewable. An LLM must never be able to cause
an order.

### Phase 17 — Production Hardening · PLANNED

Authentication, secret management, structured observability, backup and
restore, rate limiting, and a security review.

### Phase 18 — Portfolio Release · PLANNED

Decide what, if anything, can be published, and extract it under its own
licence with a full secret-history audit. Bound by
[ADR-007](../architecture/decisions/ADR-007-private-proprietary-repository.md).
