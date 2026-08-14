# ADR-004: Python for the quantitative research layer

**Status:** Accepted · **Date:** 2026-08-13 · **Phase:** 0

## Context

From Phase 8 the system needs factor research, backtesting and performance
analytics. That work is exploratory: hypotheses are cheap, most are wrong, and
the cost that matters is the time between an idea and a chart that refutes it.

The backend is C#. Adding a second language needs justification.

## Decision

Python 3.12+ as a separate layer under `quant/`, packaged with a `src` layout
and standard tooling: `pytest`, `ruff` for lint and format, `mypy` in strict
mode.

Phase 0 establishes the environment only. It has no runtime dependencies and
no financial code. The numerical stack (numpy, pandas or polars, pyarrow)
arrives in Phase 8, when there is something to compute.

## Alternatives

**C# for everything**, using Math.NET Numerics and ML.NET.

**R** for the statistical work.

**Julia**, which is genuinely well suited to this domain.

## Reasoning

The decision is about ecosystem, not language quality. Quantitative finance
tooling — statsmodels, scikit-learn, arch, PyPortfolioOpt, pyfolio, along with
every paper implementation and tutorial — is written in Python. Reimplementing
that surface in C# would be the single largest source of avoidable work and,
more importantly, of subtle statistical bugs that a well-used library has
already had found for it.

R has better classical statistics but a weaker story for the engineering half:
packaging, typing, and interoperating with a production system.

Julia is faster and arguably the better language for numerical work. It was
rejected on ecosystem maturity and on the practical point that its speed
advantage targets the tight-loop numerical case, which is the same case the
C++ layer (ADR-005) exists to cover.

Keeping the layer separate rather than embedding Python in the backend is
deliberate: research code and production request handling have different
change rates, different reliability requirements, and different failure
tolerances. A broken experiment must not be able to take the API down.

Strict `mypy` and `ruff` are non-negotiable for the same reason the C# side has
warnings-as-errors. Research code has a habit of quietly becoming production
code; typing it from the start is far cheaper than retrofitting.

## Trade-offs

- Two languages, two toolchains, two sets of conventions.
- Model and configuration definitions risk drifting between layers. Mitigated
  in Phase 0 by having both read the same `POSTGRES_*` variables; the longer
  term answer is a shared schema definition.
- Python is slow in tight loops. That is exactly what ADR-005 addresses.
- The integration mechanism is unresolved.

## Consequences

- `quant/` is an installable package; tests import the installed package, not
  the working directory, so a packaging error fails in CI rather than later.
- `pytest` runs with `filterwarnings = ["error"]`: a deprecation in a numerical
  library becomes a failing test rather than silent behaviour change.
- Domain boundaries (`research`, `factors`, `strategies`, `backtesting`,
  `analytics`) exist as real packages from Phase 0 so later code has a place to
  go that was decided deliberately.
- `POSTGRES_PASSWORD` has no default in this layer. Loading configuration
  without it raises.
- **Open:** how the backend and the quant layer exchange work — shared
  database, job queue, or local service — is a Phase 8 decision, taken when
  there is a real workload to size it against.
