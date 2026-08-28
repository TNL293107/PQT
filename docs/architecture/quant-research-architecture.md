# Quant Research Architecture

**Status: DESIGNED.** Nothing in this document is implemented. The `quant/`
package currently contains packaging, tooling and a configuration reader; it has
no financial code and no numerical dependencies. This describes what the
Research Foundation Upgrade builds there, in Gate B.

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

Added in U6. Chosen for being numerical primitives with no reasonable
substitute, not for breadth.

| Package | Role |
| --- | --- |
| `numpy`, `scipy` | Numerical foundation |
| `polars`, `pyarrow` | Dataset path — correct null semantics, lazy evaluation, native Parquet |
| `pandas` | Library edges that require it |
| `scikit-learn` | Model interface and baselines |
| `statsmodels` | Regression and diagnostics |

Optional extras: `research` — `qlib`, `mlflow`, `lightgbm`; `notebook` —
`jupyterlab`, `matplotlib`, `plotly`.

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
