# Quant Research Architecture

**Status.** The U6 sections — the protocols, the point-in-time harness and the
vertical slice — are `COMPLETE` as of 11 October 2026. Everything else here
(the boundary, the experiment store, reproduction) is **DESIGNED** and belongs
to U7–U10. This describes what the Research Foundation Upgrade builds in the
`quant/` package, in Gate B.

Roadmap and workstream numbering: [`../roadmap/pqt-roadmap-v2.md`](../roadmap/pqt-roadmap-v2.md).

---

## The pipeline

```
Universe ─→ Dataset ─→ Features ─→ Factors ─→ Signals ─→ Portfolio
                                                            ↓
        Experiment tracker ←─ Analytics ←─ Risk ←─ Backtest ─┘
```

Each stage is a contract PQT owns. A third-party engine may implement any of
them; none of them may be defined by a third-party engine.

---

## Protocols (U6)

Defined as PEP 544 `Protocol` types rather than abstract base classes. Strict
`mypy` already runs over this package, structural typing keeps implementations
decoupled from an inheritance hierarchy, and an adapter for an external library
should not have to inherit from PQT to satisfy PQT.

| Protocol | Responsibility |
| --- | --- |
| `DataProvider` | Locate a versioned dataset. Never talks to a vendor |
| `Dataset` | Load a canonical dataset by manifest id; verify its hash |
| `FeatureEngine` | Raw columns → engineered columns; declares its lookback |
| `FactorEngine` | Cross-sectional, point-in-time factor values; declares data dependencies and as-of semantics |
| `Model` | `fit` / `predict`, shaped so scikit-learn, LightGBM and an adapted Qlib model all satisfy it |
| `SignalEngine` | Factor scores → ranked signal |
| `Strategy` | Signals → target positions. Never places an order |
| `PortfolioConstructor` | Signals plus constraints → target weights |
| `Backtester` | Event-driven replay under Vietnamese market rules |
| `RiskEngine` | Ex-ante exposure and ex-post attribution |
| `ResearchValidator` | Walk-forward, out-of-sample, perturbation, overfitting statistics |
| `ExperimentTracker` | Record and retrieve reproducible runs |

All twelve are declared in `personal_quant.research.protocols`. The ones on the
vertical slice have implementations — `DataProvider` and `Dataset`
(`research.datasets`), `FactorEngine` (`factors.momentum`), `SignalEngine`
(`strategies.signals`) and `PortfolioConstructor` (`strategies.portfolio`).
`Model` has one, off the slice: Qlib's LightGBM behind an adapter
(`integrations.qlib`, U8), installed only with the `research` extra. The
rest are shapes decided once, implemented by the workstreams that own them:
`Backtester` and `ResearchValidator` by Phase 8, `ExperimentTracker` by U9.

A loaded dataset is verified before it is trusted: every file's size and
SHA-256, and the manifest's `content_hash`, recomputed exactly as the .NET
exporter renders it. The Python and .NET hashes agree on every manifest the
backend has written.

### Every data-touching protocol declares a lookback and an as-of

This is the rule that makes look-ahead **detectable** rather than merely
discouraged.

A `FeatureEngine` that computes a 20-day moving average declares a lookback of
20 sessions. Given a dataset built at `known_as_of`, the harness can then assert
mechanically that the feature reads no row whose observation time is later than
that instant. A feature that cannot state how far back it reads cannot be
checked, and an unchecked feature is where look-ahead lives.

A test in the suite plants a deliberately over-reaching feature and asserts the
harness catches it. A safeguard that has never been shown to fire is not known
to work.

Implemented in `personal_quant.research.history`. An engine never reads a
dataset; it gets a `PointInTimeHistory` cut at the as-of session and no deeper
than its lookback, and `evaluate` checks its output. Three violations raise: a
session after the as-of (`LookAheadError`), a read deeper than declared
(`LookbackExceededError`), and a score for an instrument that was not a member
on the as-of (`NonMemberScoredError`) — survivorship, caught at the same
boundary. Lookbacks count the dataset's market sessions, so a gap in one series
reads as missing rather than pulling an older bar forward.

---

## What the Python layer may and may not do

| May | May not |
| --- | --- |
| Read `quant.*` | Write `quant.*` |
| Read and write `research.*` | Define the canonical data model |
| Read Parquet datasets | Fetch from a market data provider |
| Claim and complete `research.jobs` | Reach a broker, an order path, or the OMS |

Ingestion belongs to the backend. A research process that can fetch its own data
would produce results nobody can reproduce, because the data would not be in a
dataset version.

---

## Python / .NET boundary (U10)

The question [ADR-004](decisions/ADR-004-python-quant-layer.md) left open.
Decided in [ADR-016](decisions/ADR-016-python-dotnet-research-boundary.md).

### Decision: PQT exports, Python consumes, PQT records

```
                 PostgreSQL
        ┌──────────────────────────┐
        │ quant.*    (read only)   │──────┐
        │ research.* (read/write)  │◄──┐  │
        │ research.jobs            │   │  │
        └──────────┬───────────────┘   │  │
                   │ poll              │  │ read
                   ▼                   │  ▼
        ┌──────────────────────┐   ┌───┴──────────────┐
        │  quant-worker        │   │ personal_quant   │
        │  (container)         │──►│ research code    │
        └──────────────────────┘   └───┬──────────────┘
                                       │ read
                        ┌──────────────▼───────────────┐
                        │ Parquet datasets + manifests │
                        └──────────────────────────────┘
```

Three channels, each with one job:

| Channel | Carries | Why this one |
| --- | --- | --- |
| **PostgreSQL read** | Exploration and reference data | Already there; no new moving part |
| **Parquet + manifest** | Anything that must be reproducible | Immutable and hashed. A dataset version is a file, not a database state |
| **`research.jobs` table** | Work requests | Transactional with the data, survives restarts, identical on a laptop and in Compose |

### Alternatives rejected

| Option | Why not |
| --- | --- |
| gRPC | Couples request lifetimes to research runtimes and adds a service to deploy, for no benefit until online serving exists. Revisit at Phase 18 |
| REST as the primary channel | Same coupling, weaker typing |
| Message broker | New infrastructure for a queue depth that will be single digits |
| Subprocess launched by the API | Research failures land in the API's process tree |
| Embedded Python in the API | A failing experiment takes the API down. [ADR-004](decisions/ADR-004-python-quant-layer.md) chose a separate layer precisely to prevent this |

### The boundary is enforced, not agreed

A `quant_research` PostgreSQL role holds `SELECT` on the `quant` schema and no
write grant; full rights on `research`. The test that proves the boundary is the
one asserting an `INSERT` into `quant.bars` from that role is **denied**.

A convention that only a code review enforces is not a boundary.

### `research.jobs`

```
research.jobs
  id, kind, payload jsonb, status
  claimed_by, claimed_at, lease_until
  created_at, finished_at, error
```

A worker claims a job by setting `claimed_by` and a lease. A crashed worker's
job becomes claimable again when its lease expires, so a container dying
mid-experiment loses the run, not the request. Polling latency is seconds and
irrelevant — research jobs run for minutes.

---

## Experiment store (U9)

**Implemented 11 October 2026**, as below, with `job_id` linking a run to the
job that produced it and `reproduces_run_id` marking a reproduction (which
carries no trial index and is not counted). `reproduce(run_id)` is the
`reproduce_run` job kind, queued with `pqt research reproduce`.

```
research.experiments   id, name, hypothesis, created_at

research.runs          id, experiment_id, status
                       strategy
                       universe_id, universe_as_of
                       dataset_version, schema_version
                       feature_version, factor_version, model_version
                       params_hash, code_commit, random_seed
                       train_start/end, valid_start/end, test_start/end
                       transaction_costs, slippage, execution_assumptions
                       research_engine, research_engine_version
                       trial_index, trial_count
                       started_at, finished_at

research.run_metrics   run_id, name, value, split

research.run_artifacts run_id, kind, path, sha256
```

### Why PQT owns this rather than delegating to MLflow

An MLflow run is not joinable to an instrument, does not know what a universe
is, and cannot enforce that a dataset version still exists. The questions PQT
needs to ask are SQL questions: *which runs used the dataset that contained the
mis-imported FPT split?* MLflow is used inside the Qlib adapter, where Qlib's
Recorder already writes to it, and the adapter maps the resulting run into a PQT
record. One canonical store, one optional sink.

### `trial_count` is a statistical input, not bookkeeping

The Deflated Sharpe Ratio and the Probability of Backtest Overfitting both
require the number of configurations that were tried. A Sharpe ratio of 1.8 from
one attempt and a Sharpe ratio of 1.8 selected from three hundred attempts are
not the same evidence, and the second is usually noise.

Only a complete experiment log knows the count. **This is why Phase 8 cannot
complete before Gate B** — its validation statistics take an input that only U9
produces.

---

## Reproducibility

Target property:

> Same dataset version + same code commit + same configuration ⇒ identical
> result.

Enforced by mechanism, not by documentation:

| Rule | Mechanism |
| --- | --- |
| Inputs cannot change silently | Manifest `sha256` verified on load; a mismatch is a hard error |
| The code is identified | A run refuses to start on a dirty working tree unless explicitly overridden, and the override is recorded |
| Randomness is pinned | Seeds set for `random`, `numpy` and any framework in use, and recorded |
| Numerical determinism | BLAS and LightGBM thread counts pinned and recorded — a common and silent source of drift |
| The claim is tested | `reproduce(run_id)` re-runs from the record and **diffs the metrics** |

`reproduce` matters more than the rest combined. A reproducibility system that
is never exercised does not work; it is only believed to.

---

## Dependencies

Chosen for being numerical primitives with no reasonable substitute, not for
breadth — and added when a computation needs them, not before.

**U6 added one: `pyarrow`**, because nothing else reads the dataset's Parquet.
The vertical slice's arithmetic — a 12-1 return, a rank, a quantile mean, a
rank correlation — runs on the standard library over a few dozen names, with
prices kept `Decimal` as the dataset stores them.

**U7 added a second: `duckdb`**, the analytical tier — SQL over a dataset
build, embedded, with no server to run. It queries Arrow tables in place and
holds prices as `DECIMAL`, so a query answers in the same exact arithmetic as
the Python loader. `personal_quant.research.analytical`
gives it only the bytes `LocalDatasetProvider.verify` hashed, never a path,
and opens the connection with file access off and its configuration locked:
a query that read the build from disk would read whatever is there now, not
what was verified.

The table below is where the rest is expected to come from, starting with
Phase 7.

| Package | Role |
| --- | --- |
| `numpy`, `scipy` | Numerical foundation |
| `polars`, `pyarrow` | Dataset path — correct null semantics, lazy evaluation, native Parquet |
| `pandas` | Library edges that require it |
| `scikit-learn` | Model interface and baselines |
| `statsmodels` | Regression and diagnostics |

Optional extras: `research` — `qlib`, `mlflow`, `lightgbm`; `notebook` —
`jupyterlab`, `matplotlib`, `plotly`.

**U8 added the `research` extra, and only it: `pyqlib==0.9.7`**
([ADR-017](decisions/ADR-017-qlib-research-adapter.md)). It is never a base
dependency. LightGBM, MLflow and pandas arrive with it transitively rather than
pinned beside it, because PQT uses each only through Qlib, inside
`personal_quant.integrations.qlib` — the one package allowed to import `qlib`
or `mlflow`, by a guard test in CI. pyqlib 0.9.7 publishes wheels for CPython
3.12 at the newest, so the extra needs a 3.12 interpreter while the package
itself runs on 3.12 and later. Without the extra the package and its tests run
unchanged; the adapter's engine tests skip. The `notebook` extra does not exist
yet.

**Deliberately not adopted.** Financial-semantics libraries stay out. Indicators,
trading calendars, performance metrics and backtest mechanics are PQT-owned,
because those libraries encode US market structure — no price bands, fractional
shares, T+2, US holidays — and every one of those assumptions is silently wrong
in Vietnam. A library is worth a dependency when it saves numerical work; it is
a liability when it smuggles in a market model.

---

## The deliverable that proves the layer

One complete vertical slice:

```
dataset → 12-1 momentum factor → cross-sectional rank
        → decile portfolio → return series → IC / IR
```

Not a library of factors. One factor, end to end, through every protocol, with
its lookback declared and its as-of respected. A framework with nothing running
through it is untested scaffolding, and the scope discipline here is what keeps
Gate B from becoming Phase 7 early.

**Run on 11 October 2026** over the point-in-time VN30 export
`3856e4ce77841c1a` v3 (`python -m personal_quant.research.momentum_slice`):
twelve monthly rebalances from August 2025 to August 2026, 28 of 30 members
scored each time, mean IC +0.069, IR +0.186, t +0.64, top-minus-bottom decile
positive in 8 of 12 periods. That is no evidence for or against momentum in
Vietnam — twelve periods over thirty names cannot carry it — and it was not the
point. The point is that a verified dataset went in, every factor value passed
through the point-in-time harness, and the result names the build, factor
version and parameters that produced it.

What the run exposed: a point-in-time export holds bars only inside each
spell, so a name admitted to the index has no history before its admission and
cannot be scored until a full lookback of membership has passed — two names
per rebalance here. Fixing it belongs to the export (a warm-up window of bars
before the membership window), not to the factor.
