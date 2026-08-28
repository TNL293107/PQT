# Qlib Integration

**Status: DESIGNED.** No Qlib code exists in this repository. This document
records the posture and the boundary before any is written, so that the first
adapter is built against a decided shape rather than a convenient one.

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

### Optionality

Qlib lives in the `research` optional extra, never in base dependencies. With
Qlib absent, the adapter **skips**; it does not fail. The rest of the research
layer runs unchanged, and CI's default job does not install it.

### No leaked types

No `qlib.*` type may appear in any signature, return type, dataclass field or
serialised contract outside the adapter package. Everything crossing the
boundary is a PQT type or a plain builtin.

---

## Version and dependency risk

| Fact | Source | Recorded |
| --- | --- | --- |
| Licence: MIT | Repository badge, checked 2026-08-28 | Compatible with PQT's proprietary licence; no copyleft obligation |
| Last tagged release: v0.9.0, December 2022 | Repository releases, checked 2026-08-28 | Development continues on the main branch, but the release cadence is slow |

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
2. Remove `qlib` from the `research` optional extra.
3. Delete the import guard and its test.

Nothing else changes. No PQT contract, dataset, migration or domain type refers
to Qlib. Existing experiment records remain valid and remain interpretable,
because they record the engine and version that produced them.

**If this procedure ever takes more than those three steps, the boundary has
been breached and the breach is the bug.**
