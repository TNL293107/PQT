# Roadmap

Twenty phases, ordered by dependency rather than by visible progress.
Implementation proceeds in vertical slices; the UI advances only as far as the
data behind it justifies.

**Target market: Vietnam** — HOSE, HNX, UPCOM, VN30 and Vietnamese indices.
That decision shapes instrument identity, corporate actions and the trading
rules the backtester must simulate. See
[ADR-008](../architecture/decisions/ADR-008-vietnam-market-first.md).

Status values: **COMPLETE** · **IN PROGRESS** · **PLANNED**

## The dependency chain

```
Data correct  → Research trustworthy → Backtest trustworthy
              → Risk trustworthy     → Execution trustworthy

Data wrong    → Backtest wrong → Risk wrong → Portfolio wrong → Trading wrong
```

Five phases carry that chain. None may be done superficially:

```
        Phase 2   Market Data Ingestion
            ↓
        Phase 3   Data Normalization & Quality
            ↓
        Phase 4   Corporate Actions & Adjusted Data
            ↓
        Phase 9   Backtesting Engine
            ↓
        Phase 10  Risk Engine
```

Everything else in the system rests on them.

## Milestones

| Milestone | Phases | Becomes                                          |
| --------- | ------ | ------------------------------------------------ |
| 1 — Data Foundation      | 0–4   | Already a strong project on its own   |
| 2 — Quant Platform       | 5–10  | The strongest quant-developer showcase |
| 3 — Trading System       | 11–15 | Quant developer → trading systems engineer |
| 4 — Advanced Engineering | 16–19 | C++, AI, production, presentation      |

## All phases

| Phase | Name                                | Milestone | Status   |
| ----- | ----------------------------------- | --------- | -------- |
| 0     | Foundation & Architecture           | 1         | COMPLETE |
| 1     | Instrument Master                   | 1         | COMPLETE |
| 2     | Market Data Ingestion               | 1         | COMPLETE |
| 3     | Data Normalization & Quality        | 1         | COMPLETE |
| 4     | Corporate Actions & Adjusted Data   | 1         | PLANNED  |
| 5     | Market Intelligence Terminal        | 2         | PLANNED  |
| 6     | Fundamental & Financial Data        | 2         | PLANNED  |
| 7     | News & Alternative Data             | 2         | PLANNED  |
| 8     | Quant Research Framework            | 2         | PLANNED  |
| 9     | Backtesting Engine                  | 2         | PLANNED  |
| 10    | Risk Engine                         | 2         | PLANNED  |
| 11    | Portfolio Management                | 3         | PLANNED  |
| 12    | Paper Trading                       | 3         | PLANNED  |
| 13    | Order Management System             | 3         | PLANNED  |
| 14    | Broker Integration                  | 3         | PLANNED  |
| 15    | Reconciliation                      | 3         | PLANNED  |
| 16    | C++ Performance Engine              | 4         | PLANNED  |
| 17    | AI Research Analyst                 | 4         | PLANNED  |
| 18    | Production Hardening                | 4         | PLANNED  |
| 19    | Portfolio / Public Demonstration    | 4         | PLANNED  |

---

# Milestone 1 — Data Foundation

## Phase 0 — Foundation & Architecture · COMPLETE

Build a repository another developer can clone, configure, run and understand,
with no business functionality in it.

**Delivered**

- Modular monolith backend: `Api`, `Application`, `Domain`, `Infrastructure`.
- `GET /health` (liveness) and `GET /health/ready` (readiness over PostgreSQL
  and Redis).
- EF Core + Npgsql with a baseline migration creating the `quant` schema.
- Redis connection with lazy connect and health reporting.
- React + TypeScript terminal with a live system status page.
- Python package with pytest, ruff and strict mypy.
- C++20 engine with CMake, GoogleTest and CTest.
- Docker Compose environment, verified end to end.
- GitHub Actions CI across all four stacks.
- Architecture documentation, ADRs, and this roadmap.

**Not delivered:** any financial entity, endpoint, or dataset.

## Phase 1 — Instrument Master · COMPLETE

The first phase with a real financial domain. The system must understand *what
FPT is*, not merely store the string `"FPT"`.

```
Instrument
├── instrument_id        internal canonical key
├── symbol               FPT
├── exchange             HOSE
├── asset_type           EQUITY | ETF | INDEX | FUTURES
├── currency             VND
├── status               lifecycle state
├── listing_date
├── delisting_date
└── metadata
```

Supporting entities: `Exchange`, `AssetType`, `Sector`, `Industry`,
`Currency`, `Identifier`.

Coverage: HOSE, HNX, UPCOM, VN30, indices, ETFs, futures — with room to extend
to international assets later.

**Lifecycle**

```
Pending → Listed → Suspended → Delisted
```

**Must handle:** symbol changes, exchange transfers (UPCOM → HNX → HOSE is a
normal progression in Vietnam), listing, delisting, and instrument mapping
across providers.

```
Provider → import → normalize symbol → deduplicate → Instrument Master
```

**API:** `GET /instruments`, `GET /instruments/{id}`,
`GET /instruments/search?q=`, `GET /instruments/{id}/related`.

**Done when** searching `FPT` resolves to exactly one security, and every
provider's spelling of it maps to the same canonical ID. Both now hold: one
vendor's `FPT.HM` and another's `FPT:VN` reach the same instrument, and the
second import of a symbol list creates nothing.

The ticker is never the primary key. Vietnamese tickers are reused after
delisting and change on exchange transfer, so an internal canonical ID is a
correctness requirement, not a preference.

### Workstreams

| # | Workstream | Status |
| - | ---------- | ------ |
| 1 | Instrument identity core — domain model, lifecycle, persistence | COMPLETE |
| 2 | Reference data — exchange seeding, `Sector`, `Industry` | COMPLETE |
| 3 | Identifier aliases — provider symbols, ISIN, FIGI | COMPLETE |
| 4 | Symbol normalization and deduplication | COMPLETE |
| 5 | Provider import pipeline | COMPLETE |
| 6 | Query API — list, get, search, related | COMPLETE |
| 7 | Terminal instrument search | COMPLETE |

**Workstream 1 delivered:** `Exchange` and `Instrument` aggregates with
strongly-typed identity (`InstrumentId`, `ExchangeId`) and value objects
(`Ticker`, `ExchangeCode`, `CurrencyCode`); the listing lifecycle with every
illegal transition rejected; `quant.exchanges` and `quant.instruments` with a
**partial** unique index that permits ticker reuse after delisting; repository
ports with no delete operation.

**Workstream 2 delivered:** a two-level taxonomy — `Sector` then `Industry` —
as `quant.sectors` and `quant.industries`, with an instrument pointing at an
industry and reaching its sector through it, so the two levels cannot disagree.
The link is nullable and stays that way: an index is in no industry, an
imported security may not have been mapped, and a catch-all "unknown" node
would make those indistinguishable while summing into every sector aggregate.
`GET /instruments/{id}` now returns the full record, classification included.
The seed classifies only what is not in dispute and leaves the indices, the ETF
and IDICO unclassified rather than guessing.

**Workstreams 6 and 7 delivered:** instrument search with deterministic
ranking evaluated in the database; symbol resolution reporting resolved, not
found or ambiguous; `GET /instruments/search`, `/instruments/resolve` and
`/instruments/{id}`; the terminal's Ctrl+K security search and the
current-security context every later module reads. Exchange and starter
instrument seeding for HOSE, HNX and UPCOM, behind a configuration flag.
Search-text normalisation folds Vietnamese diacritics and case, which is the
part of workstream 4 that discovery needs. See
[ADR-010](../architecture/decisions/ADR-010-instrument-search-and-security-context.md)
and the [technical reference](../architecture/instrument-search.md).

**Workstreams 3, 4 and 5 delivered:** `quant.instrument_identifiers`, holding
the aliases an instrument is known by outside this system. ISIN and FIGI are
validated by check digit and are unique across the whole master; a provider
symbol is unique only within the provider that issued it, and both rules are
partial unique indexes rather than conventions the pipeline is trusted to
follow. Search matches an alias exactly, ranked last — nobody types twelve
characters of ISIN by accident, so nothing competes with it.

Symbol normalisation splits a provider's spelling into a ticker and a venue
hint, so `FPT`, `FPT.HM`, `FPT:VN`, `HOSE:FPT` and `FPT-HNX` all resolve to the
same security. A symbol that could be two tickers is refused rather than
guessed at.

The import pipeline reconciles a source against the master:
`provider → normalise → deduplicate → match or create → record the alias`.
Deduplication is tried strongest first — this provider's own symbol, then a
global identifier, then the ticker on its venue — because a ticker is reused
after delisting and changes on an exchange transfer, so it identifies a listing
rather than a security. A row whose identifiers and symbol point at different
instruments is rejected rather than resolved: picking a side would merge two
securities or split one. Nothing is deleted, nothing is delisted, and nothing
the master already holds is overwritten.

**Workstream 6 completed:** `GET /instruments` pages the master with filters
for venue, asset class, status and sector, ordered totally so a caller sees
every row exactly once. Delisted instruments are included unless a status is
given — the opposite of search's default, because this is the read historical
work uses and omitting them is how survivorship bias enters a universe.
`GET /instruments/{id}/related` reports the instruments that have held the same
ticker on the same venue at another time.

**Deliberately absent:** a "shares an ISIN" relation. A global identifier
resolves to exactly one instrument because a Vietnamese security lists on one
venue at a time, so two instruments carrying one cannot arise, and the branch
would be one the database forbids from ever being taken. It becomes reachable
the day a cross-listed universe relaxes that constraint.

**Also absent:** an endpoint that triggers an import. It reads an external
source and writes to the system of record, and neither belongs behind an
unauthenticated route. The trigger arrives with the authentication in Phase 18.

The export format the file instrument source reads is documented in
[`data/schemas/instrument-csv.md`](../../data/schemas/instrument-csv.md).

## Phase 2 — Market Data Ingestion · COMPLETE

**Backbone phase.** Everything after it computes on what this phase stores, and
none of it can detect a fault it inherited.

```
External sources → Provider → Raw → Normalizer → Canonical → Database
                                 ↘ Rejections    ↘ Run audit + checkpoint
```

**Delivered**

- `OhlcvBar` keyed on `(instrument, interval, opening instant)` — no surrogate,
  so deduplication is the primary key rather than a rule in the writer.
- `Price` as a bounded positive decimal, stored `numeric(18,6)`. Zero is a
  provider's "no data", not a trade.
- Structural invariants on the aggregate: `high >= max(open, close)`,
  `low <= min(open, close)`, `high >= low`, `volume >= 0`, no turnover without
  volume. Failures are rejected with a reason, never repaired.
- `IMarketDataProvider` with a registry, plus a CSV file source as the
  reference implementation — a real provider under the same contract, runnable
  on a fresh clone with no licence.
- One normaliser for every source, reporting each refused row with a typed
  reason: unusable price, inconsistent prices, unusable quantity, misaligned
  timestamp, outside the requested range, duplicate within the batch.
- Raw payloads retained beside the canonical bars, checksummed, so
  re-normalising from raw is always possible.
- `IngestionRun` audit for every attempt — succeeded, failed *and* skipped —
  with fetched, accepted, rejected, stored and revised counted separately.
- `IngestionCheckpoint` per instrument, interval and source. It advances to
  the newest bar actually stored, never to the end of the requested range and
  never backwards.
- Retry with capped exponential backoff, per-call timeout, and per-source call
  spacing. Every wait goes through an injected scheduler, so the policy is
  asserted in tests rather than slept through.
- One transaction per run: payload, bars, checkpoint and audit row commit
  together. A checkpoint surviving without its bars is the one failure that
  leaves a permanent, silent hole.
- `GET /instruments/{id}/bars` and `GET /instruments/{id}/ingestion`.

**Data types**

| Level    | Content                                        | Status |
| -------- | ---------------------------------------------- | ------ |
| EOD      | Open, High, Low, Close, Volume, Value          | Delivered |
| Intraday | 1H, 30M, 15M, 5M, 1M                            | Delivered |
| Tick     | timestamp, price, quantity, side               | Out of scope |

Tick data is deliberately absent. A tick has no open, high, low or close, and
modelling it as a zero-length interval would put a row shaped like a bar into a
table that means something else.

**Driven by the host, not by HTTP.** A background service ingests the listed
universe on a configured period, and a second one runs the instrument and
calendar imports once at start-up. Both are off by default: starting the API
should not begin reading an external source. There is still no endpoint that
triggers a run — a request causing outbound calls to a rate-limited third
party waits for the authentication in Phase 18 — but a schedule the operator
configured is a different thing, and without it checkpointing and resume would
be machinery nothing exercised.

Bound by [ADR-011](../architecture/decisions/ADR-011-market-data-ingestion.md).
The export format the file source reads is documented in
[`data/schemas/market-data-csv.md`](../../data/schemas/market-data-csv.md).

## Phase 3 — Data Normalization & Quality · COMPLETE

**Backbone phase.** The point at which the project stops having *data* and
starts having *data worth running research on*.

**Structural validation** — already enforced in Phase 2, as domain invariants
on the bar itself rather than as checks something has to remember to run:

```
High  >= max(Open, Close)
Low   <= min(Open, Close)
Volume >= 0
Price  > 0
```

A row failing any of them cannot be constructed, so it never reaches storage
and never becomes a finding. **Duplicate detection** is the same: the storage
key is the instrument, resolution and opening instant, so the same period
cannot appear twice.

**Delivered**

- **Daily price limits on the venue.** HOSE ±7%, HNX ±10%, UPCOM ±15% —
  statutory market structure, seeded. The cross-session check measures against
  the venue's own band rather than a generic threshold, because the exchange
  rejects orders outside it: a larger move did not happen as printed.
- **Trading calendars** as `quant.trading_holidays`, with weekends structural
  rather than stored, and an import that reads a CSV published elsewhere.
- **Three quality rules**, all needing context a single row cannot supply:
  `PriceLimitBreach`, `MissingSession` and `UnexpectedSession`.
- **Findings recorded, never corrections applied.** The bar is kept; the
  discontinuity is written down with the numbers that triggered it, and stays
  open until something accounts for it. One finding per instrument, resolution,
  session and kind — a unique index, so a nightly run cannot re-raise it and a
  dismissal cannot be silently undone.
- **Findings commit with the bars they concern**, in ingestion's own
  transaction. A bar committed without its finding would look clean and nothing
  would know to re-check it.
- **A four-part quality score** — completeness, consistency, validity, source
  reliability — with the counts beside it, weighted into a summary for a
  dashboard.
- **Lineage on every bar:** `source`, `ingested_at`, `transformation_version`
  and `validation_version`. A restatement clears the validation stamp, because
  the values moved and what the rules concluded no longer applies.
- `GET /instruments/{id}/quality` and `/quality/issues`.

**The calendar is imported, not seeded, and that is the phase's main caveat.**
Tet and the Hung Kings commemoration follow the lunar calendar and substitute
days are set by annual decree, so Vietnam's calendar cannot be derived. Seeding
only the fixed-date holidays would be worse than seeding none: the system would
believe its calendar covers the year and report a week of real closures as
missing sessions. With no calendar, completeness is reported as **unmeasured**
and the calendar-dependent rules are skipped — the score carries a
`calendarIsComplete` flag that says which.

**Not delivered:** quality checks for intraday resolutions. The rules are
session-scoped — a price limit governs a session, not a five-minute bar — so an
intraday series is unmeasured rather than measured as good.

Bound by [ADR-013](../architecture/decisions/ADR-013-data-quality-and-lineage.md).
The calendar format is documented in
[`data/schemas/trading-calendar-csv.md`](../../data/schemas/trading-calendar-csv.md).

## Phase 4 — Corporate Actions & Adjusted Data · PLANNED

**Backbone phase.** Placed before backtesting deliberately: an unadjusted
series makes every backtest silently wrong.

**Actions:** cash dividend, stock dividend, stock split, reverse split, rights
issue, bonus shares, share issuance, symbol change.

Rights issues and bonus shares are far more common in Vietnam than in
developed markets and cannot be treated as edge cases.

```
CorporateAction
├── instrument_id
├── action_type
├── ex_date
├── record_date
├── payment_date
├── ratio
├── cash_amount
├── source
└── version
```

**Adjustment engine**

```
Raw price + corporate actions → adjustment factor → adjusted price
```

**Raw data is never overwritten.**

```
RAW  →  adjustment  →  ADJUSTED
```

Both are retained and versioned, so an adjustment error is correctable rather
than destructive.

**Guards against:** survivorship bias, look-ahead bias, incorrect split
handling, incorrect dividend handling.

**Outcome:** the backtesting engine runs on versioned,
corporate-action-adjusted historical data.

Phase 3 leaves this phase a queue to work from: every open `PriceLimitBreach`
finding is a candidate corporate action, and explaining one is a recorded
resolution rather than an edit.

---

# Milestone 2 — Quant Platform

## Phase 5 — Market Intelligence Terminal · PLANNED

The first genuinely useful interface. Not a Bloomberg clone — the goal is
information density, fast interaction, and cross-module navigation.

```
┌───────────────┬──────────────────┐
│ VNINDEX       │ Market Breadth   │
├───────────────┼──────────────────┤
│ Watchlist     │ Volume Spikes    │
├───────────────┼──────────────────┤
│ Chart         │ Signals          │
├───────────────┴──────────────────┤
│ Portfolio / PnL                  │
└──────────────────────────────────┘
```

**Command bar** as the primary interaction:

```
> FPT
> FPT financials
> FPT chart 1Y
> FPT valuation
> VNINDEX breadth
```

**Watchlist:** price, change, volume, value, foreign flow, proprietary flow.

**Market breadth:** advancers, decliners, unchanged, volume distribution.

**Volume anomaly:** current volume ÷ 20-day average.

## Phase 6 — Fundamental & Financial Data · PLANNED

Combine price with financial statements and valuation.

**Statements:** balance sheet, income statement, cash flow.

**Ratios:** P/E, P/B, EV/EBITDA, ROE, ROA, ROIC, net margin, debt/equity,
current ratio, FCF yield.

**Historical, not point-value.** Not `P/E = 12`, but P/E across 2022–2026.

**Sector comparison:** instrument vs industry median vs industry percentile.

Every fact carries both the fiscal period it describes and `reported_at`, the
moment it became public. Without that separation, look-ahead bias in Phase 9 is
unavoidable and invisible.

## Phase 7 — News & Alternative Data · PLANNED

```
News source → Collector → Normalizer → Entity extraction → Instrument mapping
```

**NLP:** sentiment classification per company, industry, market and macro
event.

**Event extraction:** identify the entity, the event type, and the sentiment.

**Alternative data:** search trends, news volume, social sentiment, foreign
flow, proprietary flow, market breadth.

Every record carries `timestamp`, `source` and `confidence`.

## Phase 8 — Quant Research Framework · PLANNED

A research platform, not just a backtester.

```python
strategy = MyStrategy(...)
result = research.run(strategy)
```

**Components:** data access, factor engine, feature engineering, signal
engine, portfolio construction, performance analysis.

**Factors:** momentum, value, quality, size, volatility, liquidity, growth.

```
Universe → calculate factors → normalize → rank → portfolio
```

**Experiment tracking.** Each run records strategy, parameters, dataset,
timestamp, code version and results, so a result can be reproduced.

Resolves the open question in
[ADR-004](../architecture/decisions/ADR-004-python-quant-layer.md): how the
backend and quant layer exchange work.

## Phase 9 — Backtesting Engine · PLANNED

**Backbone phase.** Event-driven, not vectorised.

```
Market event → Strategy → Signal → Order → Execution simulator
            → Fill → Portfolio → Risk
```

**Events:** `MarketEvent`, `SignalEvent`, `OrderEvent`, `FillEvent`,
`CorporateActionEvent`.

**Execution simulation:** commission, tax, slippage, liquidity, partial fills,
limit and market orders.

**Vietnam-specific rules that must be modelled:**

- Trading fees and sell-side tax
- Lot size (100 shares standard on HOSE)
- Daily price limits (±7% HOSE, ±10% HNX, ±15% UPCOM)
- Trading sessions, including ATO and ATC auctions
- T+ settlement
- Margin rules

A backtest that ignores T+ settlement or price limits will report returns the
market could not have produced.

**Multi-timeframe:**

```
Monthly  fundamental filter
    ↓
Weekly   factor ranking
    ↓
Intraday entry signal
```

**Output:** equity curve, drawdown, trades, exposure, turnover, costs.

## Phase 10 — Risk Engine · PLANNED

**Backbone phase.** Not "how much does the strategy make?" but "how can it
die?"

**Metrics:** Sharpe, Sortino, Calmar, maximum drawdown, VaR, CVaR, beta,
alpha, volatility.

**Portfolio risk:** position concentration, sector exposure, liquidity risk,
market exposure, factor exposure, drawdown.

**Stress testing:** VNINDEX −5% / −10%, bank sector −15%, liquidity halved.

**Limits:** max position, max sector exposure, max drawdown, max leverage, max
daily loss.

The risk engine answers one question — *may this order proceed?* — and must be
able to answer no.

---

# Milestone 3 — Trading System

## Phase 11 — Portfolio Management · PLANNED

```
Account
├── Cash
├── Positions
├── Orders
└── Trades
```

**P&L:** realised, unrealised, total.

**Attribution:** stock selection, sector allocation, market exposure, factor
exposure.

**Optimization:** minimum variance, maximum Sharpe, risk parity, efficient
frontier.

Shared by backtests and live tracking so both are measured identically.

## Phase 12 — Paper Trading · PLANNED

```
Signal → Order → Execution simulator → Portfolio
```

The bridge between backtest and live trading.

**Paper account:** cash, positions, orders, trades, P&L, fees, slippage.

**Order types:** market, limit, stop, stop-limit.

**Trading journal:** why entered, why exited, which strategy, which signal,
what risk.

## Phase 13 — Order Management System · PLANNED

```
Created → Validated → Submitted → Accepted → Partially Filled → Filled
                                ↘ Cancelled | Rejected | Expired
```

**Responsibilities:** order validation, state machine, position checks, risk
checks, duplicate prevention, idempotency, order tracking.

**A strategy never submits a broker order directly.**

```
Strategy → Signal → Portfolio → Risk Engine → OMS → Broker
```

Order events are written to PostgreSQL, not only published to Redis — the
durability requirement anticipated in
[ADR-003](../architecture/decisions/ADR-003-redis.md).

## Phase 14 — Broker Integration · PLANNED

Only after paper trading and the OMS exist.

```
        IBrokerAdapter
              │
     ┌────────┼────────┐
     ▼        ▼        ▼
  Broker A  Broker B  Broker C
```

**Operations:** submit order, cancel order, query order, query positions,
query account, and market data where the broker provides it.

**A broker failure must not crash the system.** Requires timeouts, retry,
circuit breaker, idempotency and reconciliation.

**This is the first phase in which `LIVE_TRADING_ENABLED` may be set to
`true`,** behind multiple explicit safety gates, and only after Phase 12 and
Phase 15 are complete and proven.

## Phase 15 — Reconciliation · PLANNED

Compare internal state against the broker.

```
Internal system  VS  Broker
```

Reconcile cash, positions, orders, trades and fees.

```
Internal:  FPT = 1,000 shares
Broker:    FPT =   900 shares
           → discrepancy
```

**Workflow:** detect → classify → investigate → resolve → audit.

**Nothing is silently corrected.** Every resolution leaves an audit trail.

Causes include missed fills, duplicate events, network failure, broker
rejection, partial fills and application crashes. A trading system that cannot
detect this is not trustworthy regardless of its backtest results.

---

# Milestone 4 — Advanced Engineering

## Phase 16 — C++ Performance Engine · PLANNED

**Benchmark before rewriting.** C++ is not used to look impressive.

```
Python  vs  C#  vs  C++
```

**Candidate workloads:** tick processing, order book, large-scale factor
calculation, Monte Carlo, portfolio simulation, event processing.

```
C# / Python → native interface → C++ engine
```

The goal is to demonstrate knowing *when* optimisation is necessary, not
knowing C++. Bound by
[ADR-005](../architecture/decisions/ADR-005-cpp-performance-layer.md).

## Phase 17 — AI Research Analyst · PLANNED

AI arrives late, on top of data infrastructure that already exists. Not a
chatbot that reports prices.

```
                AI Analyst
                    │
       ┌────────────┼────────────┐
       ▼            ▼            ▼
 Market data   Fundamentals    News
       └────────────┼────────────┘
                    ▼
                Research
                    ▼
              Explanation
```

Asked *"why did FPT volatility increase this month?"*, the analyst queries
price, volume, news, financials, factors and market regime, then reasons with
sources.

**Every response carries claim, source, timestamp and confidence.** The AI does
not invent data.

**Architectural boundary: the AI analyst has no path to the OMS.** It may
analyse, explain and propose hypotheses. Order sizing, risk approval and
execution remain deterministic and reviewable.

## Phase 18 — Production Hardening · PLANNED

Turns a portfolio project into an engineering system.

**Reliability:** structured logging, metrics, tracing, health checks, retry,
circuit breaker, graceful shutdown.

**Security:** authentication, authorization, secret management, rate limiting,
audit logs.

**Infrastructure:** production images, CI/CD, backup, migrations, disaster
recovery.

**SLOs:** API availability, ingestion success rate, data freshness, backtest
reproducibility, order processing latency.

## Phase 19 — Portfolio / Public Demonstration · PLANNED

The repository stays **private**. A separate portfolio surface presents the
work:

```
Portfolio site
├── Architecture
├── Screenshots
├── Technical decisions
├── Performance benchmarks
├── Backtest examples
├── Data pipeline
└── Demo video
```

Publishable: architecture diagrams, screenshots, sanitized datasets,
benchmarks, technical articles, demo video.

Never published: source code, API credentials, proprietary data, broker
credentials. Bound by
[ADR-007](../architecture/decisions/ADR-007-private-proprietary-repository.md).
