# Quant Layer

The Python side of the terminal: research, factors, strategies, backtesting
and performance analytics.

**Status:** the U6 research layer — PQT's research protocols, verified dataset
loading, the point-in-time harness, and one vertical slice (12-1 momentum →
rank → deciles → returns → IC/IR). Run the slice over an export:

```bash
python -m personal_quant.research.momentum_slice --root ../data/tmp/datasets --dataset <id>
```

And the U7 analytical tier: SQL over a verified build, through embedded DuckDB.

```python
from pathlib import Path

from personal_quant.research.analytical import connect
from personal_quant.research.datasets import LocalDatasetProvider

with connect(LocalDatasetProvider(Path("../data/tmp/datasets")), "<id>") as db:
    db.sql("SELECT dataset_version, content_hash FROM manifest").show()
    db.sql("SELECT instrument_id, max(close) FROM bars GROUP BY ALL").show()
```

`connect` verifies the build first and refuses a tampered one. The views —
`bars`, `membership`, `manifest` — read the verified bytes held in memory; the
connection cannot read files, so `read_parquet` on the build's path is refused
by design. Prices are `DECIMAL(38,10)`, never `DOUBLE`. Take results out
through Python (`fetchall`, `arrow`, `df`), not `COPY`.

And the U8 Qlib adapter: Qlib's LightGBM behind PQT's `Model` protocol, fed
rows PQT built, never Qlib's data layer (ADR-017). It needs the `research`
extra — see [The research extra](#the-research-extra-qlib).

```python
from personal_quant.integrations.qlib import LightGBMSettings, QlibLightGBM

model = QlibLightGBM(LightGBMSettings(seed=7)).fit(train_rows, train_labels)
predictions = model.predict(test_rows)  # tuple[float, ...]
model.describe()  # research_engine, research_engine_version, settings
```

## Layout

```
quant/
├── pyproject.toml              packaging, pytest, ruff and mypy configuration
├── src/personal_quant/
│   ├── environment.py          reads the shared POSTGRES_* configuration
│   ├── research/               protocols, datasets, point-in-time harness, the slice (U6);
│   │                           DuckDB over a verified build (U7)
│   ├── factors/                factor definitions — momentum (U6)
│   ├── strategies/             signals and portfolio construction (U6)
│   ├── backtesting/            close-to-close returns (U6); the backtester (Phase 8)
│   ├── analytics/              information coefficient (U6); performance (Phase 8)
│   └── integrations/qlib/      Qlib behind PQT's protocols (U8); the only place qlib is imported
└── tests/
```

The domain packages sit inside `src/personal_quant/` rather than directly
under `quant/`. A `src` layout means tests import the *installed* package
rather than whatever happens to be in the working directory, so a packaging
mistake fails in CI instead of after a release.

## The worker

`python -m personal_quant.worker` runs the research jobs the backend queues in
`research.jobs` (ADR-016): it claims one with a lease, runs its handler, and
records the result or the error, connected as the `quant_research` role. In
Compose it is the `quant-worker` service behind the `research` profile. It
reads `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DATABASE`,
`RESEARCH_DB_PASSWORD` and `PQT_DATASET_ROOT`.

The queue's SQL tests run when `PQT_TEST_DATABASE_URL` names a PostgreSQL
database (CI provides one); they build a throwaway copy of the table and drop
it afterwards.

## Setup

```bash
python -m venv .venv
```

Activate it — `.venv\Scripts\Activate.ps1` on Windows, `source .venv/bin/activate`
elsewhere — then install the package in editable mode with its dev tools:

```bash
pip install -e ".[dev]"
```

[uv](https://docs.astral.sh/uv/) works as a drop-in alternative and is
considerably faster:

```bash
uv venv && uv pip install -e ".[dev]"
```

### The research extra (Qlib)

Qlib is optional and never a base dependency. Everything above, the test suite
included, works without it; the adapter's engine tests skip and say why.
`pyqlib==0.9.7` publishes wheels for CPython 3.12 at the newest, so install the
extra into a separate 3.12 environment:

```bash
uv python install 3.12
uv venv --python 3.12 .venv-qlib
uv pip install --python .venv-qlib -e ".[dev,research]"
```

Then run the suite with that interpreter (`.venv-qlib\Scripts\python -m pytest`
on Windows, `.venv-qlib/bin/python -m pytest` elsewhere). It brings LightGBM,
pandas and MLflow, used only inside `personal_quant.integrations.qlib`. Qlib's
recorder writes to MLflow in the adapter's scratch directory — a temporary
directory unless one is given — never under the working directory. Its first
use in a process takes most of a minute (Qlib and MLflow start up, and MLflow
creates its database); later fits take about a second. Set
`MLFLOW_DISABLE_AGENT_HINT=1` to quiet an MLflow start-up message.

`import qlib` and `import mlflow` anywhere outside that package fail
`tests/test_qlib_confinement.py`.

## Checks

```bash
pytest
```

```bash
ruff check . && ruff format --check .
```

```bash
mypy
```

All three run in CI. `mypy` is configured in `strict` mode, and `pytest` turns
warnings into errors, so neither an untyped function nor a deprecation can
accumulate quietly.

## Configuration

`personal_quant.environment.load_database_settings()` reads the same
`POSTGRES_*` variables as the .NET backend, from the repository's `.env`.
`POSTGRES_PASSWORD` has no default and no fallback: if it is unset, loading
raises rather than attempting a connection with a guessed credential.
