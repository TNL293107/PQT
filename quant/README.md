# Quant Layer

The Python side of the terminal: research, factors, strategies, backtesting
and performance analytics.

**Phase 0 status:** environment and tooling only. There is no financial
computation in this package yet.

## Layout

```
quant/
├── pyproject.toml              packaging, pytest, ruff and mypy configuration
├── src/personal_quant/
│   ├── environment.py          reads the shared POSTGRES_* configuration
│   ├── research/               exploratory analysis            (Phase 8)
│   ├── factors/                factor definitions              (Phase 8)
│   ├── strategies/             signal generation               (Phase 8)
│   ├── backtesting/            historical simulation           (Phase 9)
│   └── analytics/              performance and attribution     (Phase 9)
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
