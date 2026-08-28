# ADR-018: Point-in-time market bars

**Status:** Accepted · **Date:** 2026-08-28 · **Phase:** Research Foundation Upgrade (U1)

## Context

`quant.bars` holds the canonical OHLCV series. When a provider restates a
period, `OhlcvBar.Revise()` mutates the row in place: it overwrites the prices,
sets `revised_at_utc`, increments `revision`, and resets `validation_version`.
The previous values leave the table entirely.

The row therefore records *that* a value changed and *when it last changed*,
but not *what it was before* — and it cannot answer the question a backtest has
to ask: **what did this system believe on the day the simulated strategy made
its decision?** A backtest running over a series that has since been corrected
is silently using knowledge that did not exist at the decision instant. It
looks better than reality, and nothing about the numbers says so.

`revision` looks at a glance as though it solves this. It does not. A revision
number is the ordinal identity of a statement; it carries no clock, and two
revisions can fall on either side of any given instant without the number
saying which.

Everything else needed is already in place: `IClock` is injected, the ingestion
pipeline captures one `ingestedAtUtc` per run and passes it to the normaliser,
to `Revise` and to the raw-batch retention, and `Revise` already returns `false`
when nothing changed.

## Decision

Add an append-only observation history, `quant.bar_revisions`, beside
`quant.bars`. **`quant.bars` is unchanged** — same primary key, same indexes,
same read path, same semantics as the current-best projection.

```
quant.bar_revisions
  pk (instrument_id, interval_minutes, opened_at_utc, revision)
     open, high, low, close, volume, turnover, source
     observed_from_utc            -- inclusive
     observed_to_utc  NULL        -- exclusive; NULL = currently observed
     transformation_version, validation_version
  ix (instrument_id, interval_minutes, opened_at_utc, observed_from_utc DESC)
```

As-of predicate:

```sql
observed_from_utc <= @knownAsOf
AND (observed_to_utc IS NULL OR observed_to_utc > @knownAsOf)
```

### Decision 1 — Revision-row ownership: the service, not the aggregate

`OhlcvBar` keeps sole responsibility for its own current-state mutation and
gains no child collection. `MarketDataIngestionService` creates the
`BarRevision` snapshot in exactly two places: when a new bar is added, and when
`Revise()` returns `true`.

A child collection on `OhlcvBar` was rejected. Every existing read of a bar —
the quality inspector, the adjusted-series projection, the checkpoint logic —
would acquire a navigation property it never needs, and `ListForUpdateAsync`
tracks its results, so the history would be loaded on the ingestion hot path to
be ignored. The aggregate's boundary is the bar's current state; its history is
a separate record about it, and the service that already owns the run's clock is
the natural place to write it.

### Decision 2 — Observation clock: the run instant, used twice

Both edges of a revision transition use the **same** `ingestedAtUtc` the
ingestion run already captured. No second clock read.

```
old.observed_to_utc   = ingestedAtUtc
new.observed_from_utc = ingestedAtUtc
```

so that

```
old.observed_to_utc == new.observed_from_utc
```

exactly. Two clock reads would leave a gap between the closing and opening
edges — microseconds wide, and wide enough for an as-of query landing inside it
to return nothing for a bar that has existed continuously. With one instant and
a half-open interval `[from, to)`, every instant is covered by exactly one
revision and the boundary is deterministic.

This also keeps `bars.revised_at_utc` and the new `observed_from_utc` equal by
construction rather than by coincidence, because `Revise` already receives the
same value.

### Decision 3 — Migration of existing data: seed the current statement only

**The migration is inherently lossy for historical revisions, because the
canonical store never retained intermediate values.** This is stated plainly
rather than worked around.

For a bar that has never been revised (`revision = 0`):

```
revision          = 0
observed_from_utc = ingested_at_utc      -- lossless
observed_to_utc   = NULL
```

For a bar with `revision = N >= 1` and `revised_at_utc = R`, only the current
statement can be seeded:

```
revision          = N
observed_from_utc = R
observed_to_utc   = NULL
```

**Revisions `0..N-1` are unavailable from `quant.bars` and are not
reconstructed.** An as-of query for an instant before `R` on such a bar returns
empty — which is the honest answer, and the reason the read path must never fall
back to the current value.

`ingested_at_utc` is `NOT NULL` on `bars`, so the `revision = 0` case is exact.

#### Could the raw batches reconstruct the missing revisions?

Inspected before deciding. `quant.market_data_raw_batches` retains, per run: the
verbatim provider `payload`, its `checksum`, the `source`, the
`instrument_id`, the `interval_minutes`, the requested window, and
`fetched_at_utc` — which is the *same* `ingestedAtUtc` the bars of that run were
stamped with. Nothing prunes the table.

**So the information is, in principle, sufficient**: replaying the retained
payloads in `fetched_at_utc` order through the normaliser would reproduce a
sequence of observations, including intermediate ones.

It is nonetheless rejected as part of this migration, for three reasons.

- It is a **re-normalisation job, not a schema migration.** It would have to run
  the application's normaliser over every retained payload inside a migration
  step, which is neither reversible nor safe to run during start-up.
- The replay would stamp today's `transformation_version` on values that were
  originally produced by an older normaliser, so the reconstructed history would
  be a **statement about what the current rules make of the old payload**, not a
  record of what the system actually believed. Presenting that as observation
  history would be the exact fabrication this workstream exists to prevent.
- Rejected rows are not in the payload's accepted set, so replay would not
  reproduce the stored series exactly in every case.

Reconstruction from raw batches therefore remains **available and worth building
later as an explicit, auditable backfill tool** that stamps its own
provenance — not as a silent side effect of a migration.

In this repository the point is currently moot: no production dataset exists,
and the only bars are the six synthetic fixture rows, none of which has been
revised.

### Concurrency

Inspected rather than assumed. Concurrent ingestion of the same
instrument and interval is **not currently possible**:

- `MarketDataIngestionHostedService` iterates the universe with a sequential `foreach`, awaiting each instrument.
- Its `PeriodicTimer` tick is awaited only after a pass completes, so passes never overlap.
- Compose runs a single `backend` service with no replicas, `IngestOnSchedule` defaults to `false`, and there is no HTTP trigger — the hosted service is the only ingestion path.

If it ever becomes possible, the failure mode **improves**. Today two racing
runs silently last-write-wins on `bars`. With this decision the losing
transaction violates the `bar_revisions` primary key on
`(instrument_id, interval_minutes, opened_at_utc, revision)` and fails loudly.

**No concurrency mechanism is introduced here.** No Redis, no `xmin` token. The
question belongs to U7's distributed ingestion lock, or to U3 if multi-host
ingestion arrives first.

## Alternatives

**Make `quant.bars` bitemporal** by extending its primary key with valid-time
columns.

**A single history table with a JSON snapshot** rather than typed columns.

**Rely on `market_data_raw_batches` alone** and re-normalise on demand.

**Do nothing until Phase 8** needs it.

## Reasoning

Extending the primary key of `bars` was rejected on blast radius. It doubles the
hot-path index, changes every query in the market-data, quality and adjustment
paths, and forces every Phase 2–4 test to be rewritten — which would destroy the
one piece of evidence that matters here, that the change is regression-free. A
current-state table beside an append-only history is the standard bitemporal
shape and preserves the existing read path untouched.

A JSON snapshot column was rejected because the history is queried by value —
"what was the close on that date" — and a typed column is both indexable and
checkable by the same domain invariants as the bar itself.

Relying on raw batches alone was rejected for the reasons in Decision 3: it
answers "what did the provider send" rather than "what did we believe", and the
two differ whenever the normaliser changes.

Deferring to Phase 8 was rejected because the cost grows with every day of
ingestion. The observation history of a bar cannot be reconstructed after the
fact from a store that never recorded it, and the moment the pipeline meets real
data in U3 is the moment that history starts mattering.

## Trade-offs

- A second row is written for every new bar and every genuine restatement. Bars are append-mostly and restatements are rare, so the history table grows at roughly the rate of `bars` plus a small margin.
- The current values now live in two places. The invariant that `bars` equals its open revision is a real obligation, and is asserted by a test rather than assumed.
- The migration seeds a history that is honest but incomplete for any already-revised bar. That gap is permanent unless the backfill tool is built.
- Writing the snapshot in the service means a future writer of bars that bypasses `MarketDataIngestionService` would silently skip the history. There is no such writer today, and the invariant test would catch one.

## Consequences

- `BarQuery` gains a nullable `KnownAsOfUtc`. **When it is absent, behaviour is unchanged** — the existing suite passes without modification, and that is the acceptance evidence.
- `GET /instruments/{id}/bars?knownAsOf=` reads `bar_revisions`; without the parameter it reads `bars` exactly as before.
- An as-of instant earlier than a bar's first observation returns **empty**. It never falls back to the current value.
- `Revise()` returning `false` writes no revision row and performs no temporal mutation. `Restating_a_bar_with_the_same_values_changes_nothing` stays true.
- Corporate actions are **out of scope**. The adjusted-series path still reads actions without an announcement filter, so an as-of series is point-in-time in its *prices* and not yet in its *adjustments*. That limitation is real, is documented in [`../data-architecture.md`](../data-architecture.md), and is closed by U4 — not here.
- Nothing in this decision touches universes, dataset export, the Python layer, Qlib, Redis or DuckDB.
