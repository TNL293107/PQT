# ADR-017: Qlib as a research-only adapter

**Status:** Accepted · **Date:** 2026-08-28 · **Phase:** Research Foundation Upgrade (U8)

## Context

Microsoft's Qlib is the most complete open-source quantitative research
platform available: a data layer with an expression engine, the Alpha158 and
Alpha360 feature sets, a model zoo spanning gradient boosting to transformers, a
workflow recorder backed by MLflow, a nested backtest executor, and
reinforcement-learning and online-serving components.

Rebuilding the useful parts of that would take months. Adopting it wholesale
would mean adopting its data layer, and PQT's data layer is the part of this
project that has taken the most care: canonical instrument identity, retained
raw payloads, per-bar provenance, transformation and validation versioning,
recorded quality findings, and adjustment applied on read over prices that are
never rewritten.

A decision is needed before any Qlib code is written, because the difference
between "Qlib behind an adapter" and "Qlib in the middle" is nearly invisible in
the first module and nearly irreversible by the tenth.

## Decision

**Qlib is `RESEARCH ONLY`: optional, behind an adapter, outside the production
runtime, and replaceable.**

What is taken:

| Capability | How |
| --- | --- |
| Alpha158 / Alpha360 | Adapter behind PQT's `FeatureEngine` |
| Model zoo | Adapter behind PQT's `Model` |
| Recorder / MLflow | Adapter; MLflow contained inside it, PQT's experiment store canonical |
| Expression engine, learn/infer processor split | Reference only — the ideas, not the dependency |

What is not taken: the `.bin` data layer, the backtest and nested executor,
reinforcement learning, online serving, and the high-frequency components.

PQT remains the owner of data lineage, revisions, point-in-time semantics,
instrument identity, adjustment rules, canonical datasets and research contracts.

Enforcement:

- `import qlib` may appear **only** under `quant/src/personal_quant/integrations/qlib/`, asserted by a CI check that is itself tested by planting a stray import.
- Qlib lives in the `research` optional extra, never in base dependencies. Absent Qlib, the adapter **skips** rather than fails.
- **No `qlib.*` type may appear in any PQT core, domain, application or serialised contract.**
- The Qlib version is pinned and recorded per experiment run as `research_engine_version`.

## Alternatives

**Native integration** — PQT uses Qlib directly as its research runtime.

**Qlib as PQT's data layer**, converting canonical data into `.bin` as the
working format.

**No Qlib at all** — study the abstractions and reimplement on pandas, polars
and scikit-learn.

## Reasoning

**Native integration was rejected on data integrity.** Qlib's `.bin` store is a
flat per-instrument column format carrying no provenance, no revision history,
no observation time, no rejection record and no validation versioning. PQT's
schema carries all five. Making Qlib the runtime would mean the research layer
operating on a strictly poorer representation than the one the backend
maintains, in a project whose central argument is data integrity.

**Qlib as the data layer was rejected for the same reason,** with an added one:
it would make Qlib's format the canonical model, and the canonical model is not
something to delegate to an upstream project.

**No Qlib at all was seriously considered** and rejected on cost. Alpha158 and
Alpha360 represent a large body of tested feature definitions, and the model zoo
under one `fit`/`predict` interface is the single biggest time saving available.
Behind an adapter, that value is obtainable without inheriting anything else.

**The backtest executor is not adopted, and this is not a close call.** Qlib's
exchange model has no daily price band, no T+2.5 settlement, no 100-share lot
enforcement and no foreign-ownership limit. In Vietnam those are not details at
the edge of a simulation — they *are* the simulation. A backtest that ignores a
±7% band on HOSE is not approximately right; it is answering a different
question. PQT writes its own event-driven backtester in Phase 8.

**Version risk was decisive in choosing optional over required.** Checked on
2026-08-28: the repository is MIT-licensed, which is compatible with PQT's
proprietary licence and imposes no copyleft obligation; the last tagged release
is v0.9.0 from December 2022, though development continues on the main branch.
A project that may go quiet is acceptable as an optional research engine and
unacceptable as a runtime dependency. Every constraint above follows from that
one observation.

## Trade-offs

- The adapter must generate Qlib's calendar and instrument files from a PQT dataset. That translation is real work and must never run in reverse.
- Adapter scratch data duplicates the canonical dataset on disk. It is regenerable and never a source of truth.
- Staying behind the adapter means forgoing Qlib's `qrun` workflow configuration and its nested execution framework. Both are good; neither is worth the coupling.
- The import guard is friction on a legitimate experiment that wants a Qlib utility somewhere else. That friction is the mechanism working.
- MLflow appears as a transitive concern inside the adapter. It is contained there and is not PQT's experiment store.

## Consequences

- PQT's experiment store is canonical. The adapter maps a completed Qlib run into a PQT experiment record on completion.
- CI's default job does not install Qlib. A separate job may exercise the adapter.
- Removal, if Qlib is abandoned upstream, is three steps: delete the adapter package, remove the optional extra, delete the guard and its test. **If removal ever takes more than that, the boundary has been breached and the breach is the bug.**
- Existing experiment records stay valid and interpretable after removal, because each records the engine and version that produced it.
- Detail and the removal procedure are in [`../qlib-integration.md`](../qlib-integration.md).
