# PQT Roadmap v2.0

**This is the canonical roadmap.** It supersedes the phase list previously held
in [`phases.md`](phases.md), which is now a pointer to this document. There is
one roadmap; where any other document disagrees with this one, this one is
correct and the other is a defect.

---

## Canonical project state

```
==================================================
PQT CANONICAL PROJECT STATE
==================================================
PHASE 0 — Foundation & Architecture          COMPLETE
PHASE 1 — Instrument Master                  COMPLETE
PHASE 2 — Market Data Ingestion              COMPLETE *
PHASE 3 — Data Normalization & Quality       COMPLETE *
PHASE 4 — Corporate Actions & Adjusted Data  COMPLETE *

* COMPLETE — implemented and tested, but empirically
  unvalidated against real Vietnamese market data
  until U3 is completed.
--------------------------------------------------
RESEARCH FOUNDATION UPGRADE      STATUS: IN PROGRESS
Gate A (U1–U5 + real-data validation) gates Phase 5.
Gate B (U6–U10) completes the Upgrade.
--------------------------------------------------
PHASE 5+                         STATUS: PLANNED
Phase 5 must NOT begin until GATE A is satisfied.
--------------------------------------------------
ADVANCED QUANT RESEARCH
 1. Prediction Market Mispricing   RESEARCH ONLY  (Phase 12)
 2. Information Diffusion          PLANNED        (Phase 12)
 3. Implied Risk-Neutral Distrib.  BLOCKED        (Phase 12)
 4. Backtest Overfitting — core    PLANNED        (Phase 8)
    Advanced multiple-testing      PLANNED        (Phase 12)
--------------------------------------------------
Qlib:          RESEARCH-ONLY / OPTIONAL / ADAPTER
Real VN data:  MANDATORY FOR FOUNDATION VALIDATION
PQT:           AUTHORITATIVE DOMAIN + DATA + RESEARCH CONTRACTS
==================================================
```

### The distinction the asterisk carries

```
Implemented + tested   ≠   Empirically validated with real market data
```

Phases 2–4 are built, tested and reviewed. They have never processed a real
Vietnamese price. The only market data in this repository is a six-session
synthetic series for `DEMO`, a ticker listed on no venue, written so the
pipelines can be demonstrated on a fresh clone. That makes every correctness
property those phases claim **designed and unit-tested but not empirically
falsified**, and U3 exists to close exactly that gap.

Phases 2–4 are **not** downgraded to `PLANNED` for this reason. The code is
written and the tests pass. What is missing is evidence from reality, and
saying so precisely is more useful than moving a status label.

---

## Canonical terminology

Every PQT document uses these terms and no synonyms.

| Term | Meaning |
| --- | --- |
| **Phase N** | A numbered phase, 0–20. Numbering is canonical and frozen |
| **Research Foundation Upgrade** | The U1–U10 layer between Phase 4 and Phase 5. Not a phase; it has no number |
| **Workstream U1…U10** | Upgrade workstreams. Numbering is identity, not execution order |
| **Gate A** — *Research Data Foundation* | U1–U5 plus real-data validation. Passing it permits Phase 5 to begin |
| **Gate B** — *Complete Research Foundation* | U6–U10. Passing it permits the Upgrade to be declared `COMPLETE` |
| **Event time** | When the observed period occurred — `bars.opened_at_utc` |
| **Effective time** | When a fact became true in the world — `corporate_actions.ex_date` |
| **Announcement time** | When a fact became public — `corporate_actions.announced_on` |
| **Observation time** | When PQT learned it — `bar_revisions.observed_from_utc` / `observed_to_utc` |
| **Revision** | The ordinal identity of one statement of a fact — `bars.revision`, `corporate_actions.version`. **Not a time** |
| **`knownAsOf`** | The query parameter selecting an observation-time cut |
| **Canonical dataset** | The PQT-owned research dataset defined by U5, identified by `dataset_version` + `schema_version` + manifest `sha256` |
| **Strict / permissive** | The two null-announcement policy modes (U4) |
| **Storage tiers** | *system of record* · *raw store* · *research store* · *analytical store* · *cache* |

### Status values

Only these six. No document may invent another.

`COMPLETE` · `IN PROGRESS` · `PLANNED` · `BLOCKED` · `DEFERRED` · `RESEARCH ONLY`

### Phase numbering is canonical and frozen

> **Phases are numbered 0–20. This numbering is canonical. No future agent may
> renumber, insert, merge, or invent phases, and no alternative numbering may be
> introduced.**

Capabilities may be reassigned between phases by an explicit, documented
decision. The numbers themselves do not move. A phase number in a commit
message, an ADR or a code comment must mean the same thing in five years as it
does today, and the previous roadmap's numbering drift is the reason this rule
is written down.

---

## Phase table

| # | Phase | Owns | Status |
| --- | --- | --- | --- |
| 0 | Foundation & Architecture | Layering, DI, configuration, health, Docker, CI, four toolchains | COMPLETE |
| 1 | Instrument Master | Canonical identity, listing lifecycle, sector taxonomy, identifier aliases, search | COMPLETE |
| 2 | Market Data Ingestion | Fetch, normalise, deduplicate, provenance, resume checkpoints, audit | COMPLETE * |
| 3 | Data Normalization & Quality | Trading calendar, price bands, gaps, staleness, recorded findings | COMPLETE * |
| 4 | Corporate Actions & Adjusted Data | Eight action types, adjustment factors, adjustment-on-read | COMPLETE * |
| — | **Research Foundation Upgrade** | **U1–U10; Gate A and Gate B** | **IN PROGRESS** |
| 5 | Market Intelligence Terminal | Command bar, charts, watchlist, market breadth, workspace | PLANNED |
| 6 | Fundamental & Financial Data | Statements, ratios, point-in-time fundamentals with `reported_at` | PLANNED |
| 7 | Factor Research & Feature Platform | Features, factors, signals, probability calibration and expected-value substrate | PLANNED |
| 8 | Backtesting & Research Validation | Event-driven backtest under Vietnamese market rules, **plus research validation: walk-forward, out-of-sample, perturbation, Deflated Sharpe Ratio, PBO** | PLANNED |
| 9 | Risk Engine | Ex-ante exposure, ex-post attribution, scenarios | PLANNED |
| 10 | Portfolio Management | Construction, optimisation, rebalancing | PLANNED |
| 11 | News, Events & Alternative Data | Corpus, entity mapping, event classification, sentiment | PLANNED |
| 12 | Advanced Quant Research | Information diffusion · advanced multiple-testing · implied risk-neutral distribution · prediction-market mispricing | PLANNED |
| 13 | Paper Trading | Simulated execution against live prices | PLANNED |
| 14 | Order Management System | Order lifecycle, state machine, audit trail | PLANNED |
| 15 | Broker Integration | Broker adapters, credentials, safety gates | PLANNED |
| 16 | Reconciliation | Positions, cash and fills against broker truth | PLANNED |
| 17 | C++ Performance Engine | Backtest inner loop, order book — **only after measurement**, per [ADR-005](../architecture/decisions/ADR-005-cpp-performance-layer.md) | PLANNED |
| 18 | AI Research Analyst | Research assistance and cited analysis; **never an order path** | PLANNED |
| 19 | Production Hardening | Authentication, multi-user, secrets, observability, deployment | PLANNED |
| 20 | Public Demonstration | Portfolio presentation | PLANNED |

`LIVE_TRADING_ENABLED` defaults to `false` and stays false until Phase 15.

### Why this ordering differs from the previous roadmap

News and alternative data moved later, and factor research and backtesting
moved earlier. The reason is dependency, not preference: news is the most
expensive data phase — Vietnamese-language NLP is a project in itself — and
**nothing downstream depends on it** until information diffusion in Phase 12.
Factor research depends only on prices and fundamentals, both of which exist by
Phase 6. Making the quant chain wait behind a corpus it does not need would
delay the phases that everything else in the system inherits from.

Research validation moved *into* Phase 8 rather than sitting in an
advanced-methods bucket, because a backtest without an overfitting check is not
a finished backtest.

---

## Research Foundation Upgrade

The Upgrade sits between Phase 4 and Phase 5. It exists for two reasons: to
make the Phase 0–4 data architecture **empirically valid** against real
Vietnamese market data, and to establish the research foundation that Phases
7–12 will depend on.

It is not a phase and has no number. It is a layer.

### The gate model

```
Phase 4 COMPLETE
      ↓
U1 → U2 → U3 → U4 → U5
      ↓
  ══════════════════════════════════════
   GATE A — RESEARCH DATA FOUNDATION
   U1–U5 + real-data validation
  ══════════════════════════════════════
      ↓
   ┌──────────┴──────────┐
   ↓                     ↓
Phase 5              U6 → U7 → U10 → U9 → U8
Market Intelligence        ↓
                    ══════════════════════════
                     GATE B — COMPLETE
                     RESEARCH FOUNDATION
                    ══════════════════════════
                           ↓
              Upgrade may be declared COMPLETE
```

**Phase 5 is gated by Gate A only.** Qlib, experiment tracking and the complete
Python research infrastructure do not block the Market Intelligence Terminal:
the terminal consumes data, not experiments, and holding a UI phase behind a
research adapter would be gating on the wrong thing.

**Gate B is required before the Upgrade may be called `COMPLETE`.** Until then
its status is `IN PROGRESS`, even if Phase 5 is under way.

Numbering is identity, not order. Execution order within each gate:

- **Gate A** — `U1 → U2 → U4` in sequence, with `U3` running alongside once U2 lands, then `U5`
- **Gate B** — `U6 → U7`, then `U10 → U9 → U8`

### The one cross-gate dependency

**Phase 8 cannot complete before Gate B, because it depends on U9's
`trial_count`.** The Deflated Sharpe Ratio and the Probability of Backtest
Overfitting both require the number of configurations that were tried, and only
a complete experiment log records that. Phases 5, 6 and 7 need Gate A alone;
Phase 8 needs both gates.

---

### U1 — Temporal / Point-in-Time Correctness · Gate A

**Objective.** Make "what did PQT believe at instant *T*" answerable in SQL,
without regressing any Phase 2–4 behaviour.

Five temporal concepts, never collapsed:

| # | Concept | Field | Collapsing it causes |
| --- | --- | --- | --- |
| 1 | Event time | `bars.opened_at_utc` — exists | — |
| 2 | Effective time | `corporate_actions.ex_date` — exists | Adjusting the wrong bars |
| 3 | Announcement time | `corporate_actions.announced_on` — exists, **unused** | **Look-ahead** — acting on news before it existed |
| 4 | Observation time | absent → new `bar_revisions.observed_from_utc` / `observed_to_utc` | **Revision leak** — restated history treated as originally known |
| 5 | Revision | `bars.revision`, `corporate_actions.version` — exist | Loss of *which statement* a value came from |

**Revision is not observation time and must never be used as one.** A revision
number is the ordinal identity of a statement; it carries no clock and cannot
answer "what did we know at *T*". Both are stored, both are required, and
neither substitutes for the other.

**Acceptance criterion.**

> A point-in-time query must never return information whose observation time is
> later than the requested `knownAsOf`.

Design, the T0–T3 scenario and the required tests are in
[`../architecture/data-architecture.md`](../architecture/data-architecture.md).

---

### U2 — Universe & Survivorship Correctness · Gate A

**Objective.** Make historical constituent sets a recorded fact, and let the
ingestion universe be driven by them.

The bias being eliminated:

```
today's VN30  →  backtest in 2018                              ✗ survivorship
backtest date →  historical universe → only what belonged then  ✓
```

Must support historical index membership, entry date, removal date, delisted
securities, suspended securities, historical constituent sets, and point-in-time
universe queries. The instrument master already never deletes, which solves
half the problem; recorded membership solves the other half.

**Where the honesty lives.** Historical VN30 and VNINDEX membership is
genuinely hard to source. The model is built now and populated with whatever is
obtainable; **coverage gaps are recorded as data-quality findings rather than
left to look like completeness.** An empty membership history and a complete
one must never be indistinguishable.

---

### U3 — Real Vietnamese Data Provider Integration · Gate A · **MANDATORY**

> **U3 is mandatory. It must not be downgraded, deferred, or satisfied by
> fixtures.** Synthetic data does not satisfy Gate A. No claim about Phase 2–4
> correctness may be made until U3 passes.

**Enforced layering.**

```
External provider
      ↓
Provider adapter / collector    ← provider names, units and symbology stop HERE
      ↓
Canonical PQT market data schema
      ↓
PIT · data quality · corporate actions
      ↓
Canonical research dataset
```

No provider-specific schema, field name, identifier or semantic may appear in
`PersonalQuant.Domain` or `PersonalQuant.Application`. The existing
`IMarketDataProvider` port already enforces this and U3 must not weaken it. PQT
must be able to switch providers without touching the domain model or the
research contracts.

`FileMarketDataProvider` **remains permanently available** as the deterministic
fallback and test provider. It is not scaffolding awaiting removal.

Provider evaluation — the capability matrix, the seven questions that must be
kept separate, and the licensing position — is recorded in
[ADR-015](../architecture/decisions/ADR-015-vietnam-market-data-provider.md).

**Also in scope**, because real data makes them prerequisites rather than
improvements: a bulk-load path for backfill, a real Vietnamese trading calendar
including Tet with calendar import enabled, and telemetry on the ingestion path.

---

### U4 — Adjustment & Announcement Awareness · Gate A

**Objective.** Close look-ahead in the adjustment path, and catch action data
that was transcribed wrongly.

The Phase 4 shape is correct and does not change:

```
raw data + corporate action events + versioned adjustment rules  →  adjusted view (on read)
```

What changes is the read. The cumulative factor product is taken over the
actions with `announced_on <= knownAsOf`, and the null-announcement policy is
explicit rather than implied:

| Mode | Null `announced_on` | Default for |
| --- | --- | --- |
| **Strict** | Excluded | Backtests, dataset export |
| **Permissive** | Included | Charting, terminal display |

Every response states which mode and which `knownAsOf` produced it. "We do not
know when this was announced" must never silently become "we always knew".

U4 also closes the gap Phase 4 recorded against itself: an action whose implied
factor does not correspond to an observed discontinuity raises a data-quality
finding, reusing Phase 3's machinery rather than inventing a mechanism.

---

### U5 — Canonical Dataset Contract · Gate A

**Objective.** Define the research dataset PQT owns, so that no third-party
research framework can become the canonical data model.

The manifest carries instrument identity, timestamps, OHLCV, corporate actions,
adjustment state, provider lineage, revision and version, PIT semantics,
`dataset_version` and `schema_version`. Full field list in
[`../architecture/data-architecture.md`](../architecture/data-architecture.md).

**Boundary against U7.** U5 defines the contract and writes Parquet to a
configured directory, which needs no new infrastructure. U7 decides the broader
storage architecture. The two gates do not collide.

---

### U6 — Quant Research Abstraction Layer · Gate B

**Objective.** PQT-owned, framework-agnostic research contracts. **PQT owns the
abstractions; third-party engines implement adapters against them.** Qlib's
architecture is studied, not copied.

Protocols: `DataProvider` · `Dataset` · `FeatureEngine` · `FactorEngine` ·
`Model` · `SignalEngine` · `Strategy` · `PortfolioConstructor` · `Backtester` ·
`RiskEngine` · `ResearchValidator` · `ExperimentTracker`.

Every protocol that touches data declares a lookback and an as-of. A feature
that cannot state how far back it reads cannot be validated against a
point-in-time dataset, and that is what makes look-ahead *detectable* rather
than merely discouraged.

The deliverable that proves the layer is one complete vertical slice — dataset
to factor to rank to portfolio to return series to IC/IR. A framework with no
strategy through it is untested scaffolding.

Detail in
[`../architecture/quant-research-architecture.md`](../architecture/quant-research-architecture.md).

---

### U7 — Research Storage · Gate B

| Tier | Technology | Justification |
| --- | --- | --- |
| System of record | PostgreSQL 17, `quant` schema | Unchanged; correct |
| Raw store | PostgreSQL today, behind a payload-store seam | Object storage becomes an implementation, not a migration |
| Research store | Parquet — immutable, versioned, hashed | A dataset version becomes a file with a hash, not a database state |
| Analytical store | DuckDB, embedded | Reads Parquet natively; no server, no operations |
| Experiment store | PostgreSQL, `research` schema | Must be transactional and joinable to instruments |
| Cache | Redis | Given a real job — a distributed ingestion lock and manifest caching — or removed from the readiness gate |

**Rejected, with the trigger that would reverse it.** TimescaleDB and
ClickHouse. Daily bars for roughly 400 Vietnamese tickers across fifteen years
is about 1.5 million rows; PostgreSQL will not notice. Revisit at one-minute
bars across more than 500 instruments over more than three years — roughly 150
million rows — or when a range query on `bars` exceeds about a second.

Qlib's `.bin` format is adapter scratch space and never a PQT storage tier.

---

### U8 — Qlib Research Adapter · Gate B

**Posture: research-only, optional, outside the production runtime,
replaceable.** Recorded in
[ADR-017](../architecture/decisions/ADR-017-qlib-research-adapter.md) and
detailed in
[`../architecture/qlib-integration.md`](../architecture/qlib-integration.md).

PQT remains the owner of data lineage, revisions, PIT semantics, instrument
identity, adjustment rules, canonical datasets and research contracts. Qlib is
an adapter and must be removable without redesigning PQT.

---

### U9 — Experiment Tracking & Reproducibility · Gate B

**Objective.** *Same dataset version + same code commit + same configuration ⇒
identical result.*

Every run records its experiment, strategy, universe and universe as-of,
dataset version, schema version, feature/factor/model versions, parameters,
code commit, random seed, training/validation/test windows, transaction costs,
slippage, execution assumptions, research engine and engine version, results,
metrics, artifacts and timestamps — and **`trial_count`**.

`trial_count` is not bookkeeping. It is a statistical input: the Deflated
Sharpe Ratio and PBO both require the number of configurations tried, and only
the experiment log can supply it. Phase 8 depends on this column existing.

---

### U10 — Python / .NET Research Boundary · Gate B

**Objective.** Close the question [ADR-004](../architecture/decisions/ADR-004-python-quant-layer.md)
left explicitly open.

Decision: **PQT exports, Python consumes, PQT records.** Python reads
PostgreSQL and Parquet; a `research.jobs` table owned by .NET migrations is
polled by a worker container; Python writes only into the `research` schema.

Enforced by mechanism rather than convention: a research database role with
read access to `quant` and no write grant. Recorded in
[ADR-016](../architecture/decisions/ADR-016-python-dotnet-research-boundary.md).

---

## Dependency graph

```
Phase 0 → 1 → 2 → 3 → 4
                       ↓
        ┌──── RESEARCH FOUNDATION UPGRADE ────┐
        │  U1 PIT                             │
        │    ↓                                │
        │  U2 Universe ──→ U3 Real VN data    │
        │    ↓                                │
        │  U4 Adjustment / announcement       │
        │    ↓                                │
        │  U5 Canonical dataset contract      │
        └──────────────┬──────────────────────┘
                       ↓
                ═══ GATE A ═══
                       ↓
        ┌──────────────┴──────────────┐
        ↓                             ↓
   Phase 5                      U6 Abstraction
   Market Intelligence                ↓
        ↓                       U7 Research storage
   Phase 6 Fundamentals               ↓
        ↓                       U10 Boundary
   Phase 7 Factor Research            ↓
        ↓                       U9 Experiments
   Phase 8 Backtesting +              ↓
           Research Validation   U8 Qlib adapter
        ↓         ↘                   ↓
   Phase 9 Risk    Phase 11    ═══ GATE B ═══
        ↓          News/Events
   Phase 10 Portfolio  ↙
        ↓            ↙
   Phase 12 Advanced Quant Research
        ↓
   Phase 13 Paper Trading → 14 OMS → 15 Broker → 16 Reconciliation
        ↓
   Phase 17 C++ → 18 AI Analyst → 19 Production → 20 Demonstration
```

Phase 8 also depends on Gate B, through U9. That edge is not drawn above
because it crosses the diagram; it is stated in
[the cross-gate dependency](#the-one-cross-gate-dependency) and must not be
forgotten.

---

## Advanced quant research — ownership matrix

Every capability has **exactly one** canonical owner. No additional phases are
created for them, and no ownership is duplicated across Phases 7, 8, 11 or 12.

| Capability | Canonical owner | Status | Prerequisites |
| --- | --- | --- | --- |
| Prediction Market Mispricing | **Phase 12** | `RESEARCH ONLY` | Calibration and expected-value substrate (Phase 7); a legally usable, sufficiently liquid market |
| Information Diffusion | **Phase 12** | `PLANNED` | U1 announcement time · Phase 8 event-study framework · Phase 11 news and event corpus · entity-to-instrument mapping |
| Implied Risk-Neutral Distribution (Breeden–Litzenberger) | **Phase 12** | `BLOCKED` | Options instrument and quote/surface model; adequate options, strike and quote data |
| Backtest Overfitting Detection — **core** | **Phase 8** | `PLANNED` | U1 · U2 · U9 `trial_count` · Phase 7 |
| Advanced multiple-testing (Reality Check, SPA, large-scale CSCV) | **Phase 12** | `PLANNED` | Phase 8 plus a genuine strategy library |

Rationale and prerequisites in
[`../architecture/advanced-research.md`](../architecture/advanced-research.md).

---

## Definition of Done

### Gate A — Research Data Foundation · *gates Phase 5*

- **U1** — as-of reads work; no historical query can see future information
- **U2** — historical constituents queryable as of a date; survivorship addressed; coverage gaps recorded as findings
- **U3** — provider adapter works; raw data preserved; canonical mapping works; instrument identity works; **real Vietnamese trading calendar including Tet** enabled; quality findings generated **from real data**; provider capability matrix filled in with verified values; ADR-015 written
- **U4** — raw events preserved; adjustments versioned; adjusted views reproducible; implausible actions flagged
- **U5** — canonical dataset contract with manifest, schema version, dataset version and hash verification
- **Real Vietnamese market data passing through Phases 2–4 end to end**

Once Gate A passes, Phase 5 may begin.

### Gate B — Complete Research Foundation · *gates declaring the Upgrade COMPLETE*

- **U6** — quant abstraction with one factor end to end
- **U7** — research storage working; Redis given a job or removed from readiness
- **U8** — Qlib adapter working **and provably optional**
- **U9** — experiment provenance complete, including `trial_count`
- **U10** — boundary working, with Python write access to `quant.*` **provably denied**
- Reproducibility workflow — a recorded run can be re-executed and its metrics diffed
- Dataset hashing and verification enforced on load

---

## Documentation scope

| Document | Owns |
| --- | --- |
| This file | Phases, workstreams, gates, statuses, ownership, definition of done |
| [`../architecture/data-architecture.md`](../architecture/data-architecture.md) | Temporal model, universe model, adjustment rules, dataset contract, storage tiers |
| [`../architecture/quant-research-architecture.md`](../architecture/quant-research-architecture.md) | Research protocols, pipeline, Python/.NET boundary, experiment schema, reproducibility |
| [`../architecture/qlib-integration.md`](../architecture/qlib-integration.md) | Qlib adapter boundary, ownership split, removal procedure |
| [`../architecture/advanced-research.md`](../architecture/advanced-research.md) | The five advanced research capabilities |
| [`../architecture/data-policy.md`](../architecture/data-policy.md) | Market data licensing rules and the provider evaluation checklist |
| [`../architecture/decisions/`](../architecture/decisions/) | Decisions that were expensive to make and would be expensive to reverse |

---

## Deferred and rejected technologies

Recorded so that a future agent does not re-litigate a settled question, and so
that a reversal is a decision rather than a drift.

| Technology | Position | Reason |
| --- | --- | --- |
| TimescaleDB, ClickHouse | `DEFERRED` | No workload justifies them at current scale. Trigger to revisit is stated in U7 |
| Message broker (Kafka, RabbitMQ) | `DEFERRED` | The `research.jobs` table covers the only asynchronous need. A broker is infrastructure with no workload |
| gRPC between .NET and Python | `DEFERRED` | Revisit at Phase 18 when online serving creates a synchronous need |
| Embedded Python in the API process | Rejected | A failing experiment would take the API down, contradicting [ADR-004](../architecture/decisions/ADR-004-python-quant-layer.md) |
| Qlib as data layer, backtester or runtime | Rejected | PQT's data model carries lineage, revisions and PIT semantics that Qlib's format does not. See [ADR-017](../architecture/decisions/ADR-017-qlib-research-adapter.md) |
| Third-party backtesters as PQT's engine | Rejected | Vietnamese price bands, lot sizes and settlement are the simulation, not a detail |
| C++ for factor or indicator computation | Rejected | The numerical stack already dispatches to vectorised native code. [ADR-005](../architecture/decisions/ADR-005-cpp-performance-layer.md) requires a measurement first |

---

## What this roadmap does not claim

- It does not claim PQT has been validated against real Vietnamese market data. It has not. U3 exists for that.
- It does not claim any provider is licensed, suitable, or legally usable. ADR-015 records what has been verified and what has not.
- It does not claim the four advanced research capabilities are designed in detail. Their prerequisites are placed; their designs are not written.
