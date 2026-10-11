# Quant Layer

The Python side of the terminal: research, factors, strategies, backtesting
and performance analytics.

**Status:** the U6 research layer — PQT's research protocols, verified dataset
loading, the point-in-time harness, and one vertical slice (12-1 momentum →
rank → deciles → returns → IC/IR). Run the slice over an export:

```bash
python -m personal_quant.research.momentum_slice --root ../data/tmp/datasets --dataset <id>
```

## Layout

```
quant/
├── pyproject.toml              packaging, pytest, ruff and mypy configuration
├── src/personal_quant/
│   ├── environment.py          reads the shared POSTGRES_* configuration
│   ├── research/               protocols, datasets, point-in-time harness, the slice (U6)
│   ├── factors/                factor definitions — momentum (U6)
│   ├── strategies/             signals and portfolio construction (U6)
│   ├── backtesting/            close-to-close returns (U6); the backtester (Phase 8)
│   └── analytics/              information coefficient (U6); performance (Phase 8)
└── tests/
```

The domain packages sit inside `src/personal_quant/` rather than directly
under `quant/`. A `src` layout means tests import the *installed* package
rather than whatever happens to be in the working directory, so a packaging
mistake fails in CI instead of after a release.

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
