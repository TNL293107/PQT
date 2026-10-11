# Qlib Integration

**Status: IMPLEMENTED (model adapter), 11 October 2026.** The posture and the
boundary below were recorded before any Qlib code was written, so that the
first adapter was built against a decided shape rather than a convenient one.
The first adapter — Qlib's LightGBM behind PQT's `Model` — now exists, and
[What exists](#what-exists) says exactly how far it goes.

Decision: [ADR-017](decisions/ADR-017-qlib-research-adapter.md).
Workstream: **U8**, Gate B — [`../roadmap/pqt-roadmap-v2.md`](../roadmap/pqt-roadmap-v2.md).

---

## Posture

**Qlib is `RESEARCH ONLY` · optional · behind an adapter · replaceable.**

Qlib is not, and must never become:

- PQT's canonical data model
- PQT's production backtester
- part of PQT's production runtime
- a dependency of `PersonalQuant.Domain`, `PersonalQuant.Application`, or the `personal_quant` core
- a replacement for PQT's research contracts

---

## What PQT owns, permanently

```
PQT owns:
  data lineage
  revisions
  point-in-time semantics
  instrument identity
  adjustment rules
  canonical datasets
  research contracts
```

None of these may be delegated. They are the properties that make PQT's data
trustworthy, and they are precisely the properties Qlib's data layer does not
carry.

---

## The boundary

```
PQT canonical point-in-time dataset
            ↓
     versioned Parquet + manifest
            ↓
personal_quant.integrations.qlib      ← the ONLY module that imports qlib
            ↓
Qlib: DatasetH · model zoo · Recorder (MLflow, local)
            ↓
        run mapper
            ↓
PQT experiment store  (research schema — canonical)
```

Data flows **into** Qlib from a PQT dataset. Results flow **out** into a PQT
experiment record. Qlib sits in the middle and holds nothing PQT needs.

As built for the model adapter, the middle is narrower than the sketch above:
Qlib is not handed a dataset at all, only rows of numbers PQT has already
built through its point-in-time harness.

```
PQT verified dataset → point-in-time harness (evaluate) → feature rows + labels
            ↓                                            (PQT decides rows, as-of, labels)
QlibLightGBM.fit / predict                 ← personal_quant.integrations.qlib
            ↓   in-memory stand-in answering LGBModel's `segments` / `prepare`
qlib.contrib.model.gbdt.LGBModel           (Recorder → MLflow, SQLite, in scratch)
            ↓
tuple[float, ...] predictions · training curve · describe() → builtins
```

---

## What exists

**Implemented 11 October 2026** — `quant/src/personal_quant/integrations/qlib/`:

| Piece | What it does |
| --- | --- |
| `QlibLightGBM` | Satisfies `research.protocols.Model` (`fit(features, labels)`, `predict(features)`), checked structurally by strict mypy. Qlib's `LGBModel` does the fitting. Optional keyword-only `validation=(rows, labels)` enables early stopping. |
| `_Segments` | Duck-types the two things `LGBModel` asks of a `DatasetH` — `segments` and `prepare` — from PQT's rows. Not a Qlib type; Qlib's data layer is never imported. |
| `LightGBMSettings` | Frozen; rounds, early stopping, learning rate, leaves, min rows per leaf, seed, threads. LightGBM runs single-threaded and `deterministic`. |
| `describe()` | `research_engine` (`qlib`), `research_engine_version` (`0.9.7`), the engine model class, the LightGBM version, and the settings — builtins, ready for U9's run record. |
| `available()`, `engine_version()`, `QlibUnavailableError` | Answer "is Qlib here, which one" without importing it; a call that needs the engine without it raises with the install command. |

**The session Qlib needs.** `LGBModel.fit` logs every round through Qlib's
Recorder `R`, which exists only after `qlib.init`. The adapter initialises Qlib
once per process into a scratch directory (a temporary directory by default):
`provider_uri` is an empty directory there — required by `qlib.init`, never
read — and MLflow tracks into a SQLite file there, because MLflow 3 refuses its
file-system store. The adapter creates the MLflow experiment itself, with its
artifact location in the scratch: one created by Qlib puts artifacts — the
recorder saves the working directory's `git` status and diff with every run —
under `./mlruns` wherever the process runs. `qlib.init` also installs an exit
handler and `sys.excepthook` to close runs; both are process-wide, and a
research process is the only kind that imports the adapter.

**Not built yet.** Alpha158 / Alpha360 behind `FeatureEngine` (they are
expressions over Qlib's data layer, so they need the calendar and instrument
export ADR-017 describes, into adapter scratch, before they can run), the run
mapper into PQT's experiment store (U9 owns the store), and adapters for other
zoo models. Each goes in the same package, behind the same guard.

**Evidence.** In a Python 3.12 environment with the extra installed (pyqlib
0.9.7, LightGBM 4.7.0, MLflow 3.17.0, pandas 3.0.6): the whole quant suite
passes, the adapter's own tests included — it learns a planted relationship
(correlation above 0.9 on unseen rows), repeats a fit to identical predictions,
returns only builtin floats, stops early on validation rows, and writes nothing
outside its scratch. Run walk-forward over the real VN30 export (three momentum
windows from the harness as features, the 21-session forward return as label,
first six rebalances to train, last six to test) it trained on 168 rows and
predicted every test period — the plumbing proven, no claim about the model.

---

## What is taken, and what is not

| Qlib capability | Position | Owner of the contract |
| --- | --- | --- |
| Alpha158 / Alpha360 feature sets | Adapter behind `FeatureEngine` | PQT |
| Model zoo (GBDT, LSTM, Transformer, …) | Adapter behind `Model` | PQT |
| Recorder / MLflow | Adapter; MLflow contained inside it, PQT's store canonical | PQT |
| Expression engine | Reference only — studied, not depended on | PQT |
| `DataHandler` learn/infer processor split | Reference only — the *idea* is borrowed as a PQT concept | PQT |
| `.bin` data format | Export only, into adapter scratch space | PQT |
| Backtest and nested executor | **Not integrated** | PQT (Phase 8) |
| Reinforcement learning, online serving, high-frequency | **Not integrated** | — |

### Why the data layer is not adopted

Qlib's `.bin` store is a flat, per-instrument column format. It carries no
provenance, no revision history, no observation time, no rejection record and no
validation versioning. PQT's schema carries all five. Adopting Qlib's data layer
would be a measurable downgrade in data integrity, in a project whose entire
argument is data integrity.

### Why the backtester is not adopted

Qlib's exchange model has no daily price band, no T+2.5 settlement, no
100-share lot enforcement and no foreign-ownership limit. In Vietnam those are
not details around the edge of a simulation — they *are* the simulation. A
backtest that ignores a ±7% band on HOSE is not approximately right; it is
answering a different question.

---

## Enforcement

### The import guard

`import qlib` may appear **only** under
`quant/src/personal_quant/integrations/qlib/`.

A CI check asserts this across the tree and fails the build on a stray import.
The check is itself tested by planting an import in a temporary file and
confirming the check fires — a guard that has never been shown to fire is not
known to work.

*As built:* `quant/tests/test_qlib_confinement.py`, run by `pytest` in the
default CI job. It parses every `.py` under `quant/src` and `quant/tests` and
flags `import qlib…`, `from qlib… import`, and `importlib.import_module` /
`__import__` with a literal `qlib…` name, nested or not; text in a docstring or
comment is not an import. `mlflow` is guarded the same way, since it arrives
with Qlib and stays in the adapter. Eleven planted imports are each caught; the
adapter's directory, relative imports and look-alike names (`qlibrary`) are
not, and a sibling directory named `qlib_extras` is not exempt.

### Optionality

Qlib lives in the `research` optional extra, never in base dependencies. With
Qlib absent, the adapter **skips**; it does not fail. The rest of the research
layer runs unchanged, and CI's default job does not install it.

*As built:* `research = ["pyqlib==0.9.7"]`. The adapter imports Qlib lazily, so
`personal_quant.integrations.qlib` imports without it; `available()` is then
`False` and a call that needs the engine raises `QlibUnavailableError` naming
the extra. `quant/tests/test_qlib_optional.py` proves it in fresh interpreters:
with Qlib made unimportable (`sys.modules["qlib"] = None`), every module of the
package still imports; with it importable, importing every module loads none
of `qlib`, `mlflow` or `lightgbm`. The adapter's engine tests skip with the
reason and install command. A separate CI job, `quant-research`, installs the
extra on Python 3.12 and runs them; it is not required by the `ci` gate.

### No leaked types

No `qlib.*` type may appear in any signature, return type, dataclass field or
serialised contract outside the adapter package. Everything crossing the
boundary is a PQT type or a plain builtin.

*As built:* a test resolves the type hints of everything the adapter exports —
functions, methods, properties, dataclass fields — and fails on any type from
`qlib`, `pandas`, `numpy`, `lightgbm` or `mlflow`. At run time the adapter
converts what Qlib returns (a pandas `Series`, LightGBM's metric lists) to
`tuple[float, ...]` and builtin mappings before returning.

---

## Version and dependency risk

| Fact | Source | Recorded |
| --- | --- | --- |
| Licence: MIT | Repository badge, checked 2026-08-28 | Compatible with PQT's proprietary licence; no copyleft obligation |
| Last tagged release: v0.9.0, December 2022 | Repository releases, checked 2026-08-28 | Development continues on the main branch, but the release cadence is slow |
| Pinned: `pyqlib==0.9.7` | PyPI, resolved as the newest release for CPython 3.12 on 2026-10-11 | Wheels for CPython 3.8–3.12 only; on 3.13+ the extra does not install (uv: "no wheels with a matching Python ABI tag … `cp314`"). The extra therefore needs a 3.12 interpreter |
| Transitive, unpinned: MLflow 3.17.0, LightGBM 4.7.0, pandas 3.0.6 | Installed with the extra, 2026-10-11 | MLflow 3 already broke the obvious setup — it refuses its file store — and the adapter uses SQLite; `describe()` records the LightGBM version because it decides the trees |

The gap between the last tag and current activity is the reason for every
constraint above. A project that may go quiet is acceptable as an optional
research engine and unacceptable as a runtime dependency. The adapter must be
pinned to a specific version or commit, and that pin recorded in the experiment
store as `research_engine_version`, so a result can always be traced to the
engine that produced it.

---

## Removal procedure

If Qlib is abandoned upstream, becomes incompatible, or is simply no longer
worth the maintenance, removal is:

1. Delete `quant/src/personal_quant/integrations/qlib/`.
2. Remove `pyqlib` from the `research` optional extra (and its mypy override).
3. Delete the import guard and its test — `quant/tests/test_qlib_confinement.py`
   — with the adapter's tests, `test_qlib_adapter.py` and `test_qlib_optional.py`,
   and the `quant-research` CI job that runs them.

Nothing else changes. No PQT contract, dataset, migration or domain type refers
to Qlib. Existing experiment records remain valid and remain interpretable,
because they record the engine and version that produced them.

**If this procedure ever takes more than those three steps, the boundary has
been breached and the breach is the bug.**
