# Data Architecture

**Status: DESIGNED.** This document describes the data architecture the
Research Foundation Upgrade delivers. Sections marked *exists* are implemented
in Phases 0–4; everything else is designed and not yet written. Nothing here is
a claim about running code unless it says *exists*.

Roadmap and workstream numbering: [`../roadmap/pqt-roadmap-v2.md`](../roadmap/pqt-roadmap-v2.md).

---

## The pipeline

```
External provider
      ↓
Provider adapter / collector      ← provider names, units, symbology stop HERE
      ↓
RAW batch            quant.raw_market_data_batches        exists
      ↓
Normalisation + rejection reasons                          exists
      ↓
CANONICAL bars       quant.bars  (current best value)      exists
      ↓
Observation history  quant.bar_revisions (append-only)     U1
      ↓
AS-OF view           what PQT believed at an instant       U1
      ↓
Announcement filter  actions with announced_on <= as-of    U4
      ↓
ADJUSTED-AS-OF series                                      U4
      ↓
Universe filter      constituents as of the same instant   U2
      ↓
CANONICAL DATASET    Parquet + manifest, hashed            U5
      ↓
Features · factors · models · backtests                    U6+
```

Each stage is additive. No stage rewrites the one above it, which is why an
error at any level is corrected by recomputing downstream rather than by
restoring a backup.

---

## Time

### Five concepts, never collapsed

| # | Concept | Answers | Field | Status |
| --- | --- | --- | --- | --- |
| 1 | **Event time** | When did the period occur? | `bars.opened_at_utc` | exists |
| 2 | **Effective time** | When did the fact become true in the world? | `corporate_actions.ex_date` | exists |
| 3 | **Announcement time** | When did it become public? | `corporate_actions.announced_on` | exists, **unused** |
| 4 | **Observation time** | When did PQT learn it? | `bar_revisions.observed_from_utc` / `observed_to_utc` | U1 |
| 5 | **Revision** | Which statement of the fact is this? | `bars.revision`, `corporate_actions.version` | exists |

Collapsing any pair produces a specific, named defect:

| Collapse | Defect |
| --- | --- |
| Announcement into effective | **Look-ahead.** A strategy acts on a corporate action before it was public |
| Observation into event | **Revision leak.** A restated price is treated as though it had always read that way |
| Revision into observation | **Unanswerable history.** You know a value changed but not when you learned it |
| Effective into event | Adjusting the wrong bars |

### Revision is not a timestamp

A revision number is the **ordinal identity of a statement**. It says *which*
version of a fact this is. It carries no clock, so it cannot answer "what did we
know at *T*" — two revisions may be observed in either order relative to any
given instant, and the number alone will not say.

Both are stored. Both are required. Neither substitutes for the other. This is
written down because `bars.revision` and `bars.revised_at_utc` already exist and
look, at a glance, as though they solve point-in-time. They do not: `Revise()`
overwrites the row, so the previous value leaves `quant.bars` entirely.

---

## Point-in-time reads (U1)

### Design

`quant.bars` stays exactly as it is — the current-best projection, same primary
key, same indexes, same read path. An append-only history table is added beside
it:

```
quant.bar_revisions
  pk (instrument_id, interval_minutes, opened_at_utc, revision)
     open, high, low, close, volume, turnover, source
     observed_from_utc              -- inclusive
     observed_to_utc  NULL          -- exclusive; NULL means still current
     transformation_version, validation_version
```

`Revise()` closes the open revision by setting `observed_to_utc`, then appends
the next one. The as-of predicate is:

```sql
observed_from_utc <= @knownAsOf
AND (observed_to_utc IS NULL OR observed_to_utc > @knownAsOf)
```

**Invariant:** revision 0's `observed_from_utc` equals the bar's
`ingested_at_utc`. The two views of first observation must agree, and a test
asserts it.

**Alternative rejected.** Making `quant.bars` itself bitemporal by extending its
primary key with valid-time columns. It doubles the hot-path index, rewrites
every existing query and every Phase 2–4 test, and buys nothing that a
current-state table plus an append-only history does not already give.

### The scenario the design must satisfy

```
T0   Provider reports    Close = 100
T1   PQT observes and stores   Close = 100
T2   Provider revises    Close = 101
T3   PQT observes the revision

Query knownAsOf = T1   →  Close = 100
Query knownAsOf = T3   →  Close = 101
Current query          →  Close = 101
```

`knownAsOf = T1` returns 100 because `observed_from_utc` is **inclusive** at the
observation instant.

### `knownAsOf`

`BarQuery` gains a nullable `KnownAsOfUtc`, surfaced as
`GET /instruments/{id}/bars?knownAsOf=`.

**When it is absent the behaviour is byte-identical to today.** That is what
makes the change safe to land: every existing test passes unmodified, and the
new path is opt-in.

### Required tests

1. A revision **before** an as-of returns the new value; a revision **after** it returns the old one.
2. A query **before first observation** returns empty — not an error, and not a fallback to the current value.
3. **Multiple revisions** of one bar: each as-of window returns exactly the statement current at that instant.
4. **Current projection versus historical revision**: `quant.bars` and the open `bar_revisions` row never disagree.
5. **No future observation is visible** to an earlier query — a property test over randomised revision sequences.
6. **Zero regression**: the existing suite passes unmodified with `knownAsOf` absent.

### Acceptance criterion

> A point-in-time query must never return information whose observation time is
> later than the requested `knownAsOf`.

---

## Universe and membership (U2)

```
quant.universes
  id, code (VN30 | VNINDEX | HOSE_ALL | …), name, kind, source

quant.universe_memberships                      -- append-only, never deleted
  universe_id, instrument_id
  effective_from, effective_to NULL             -- NULL means still a member
  announced_on, source
```

Queried as `constituents(universe, as_of)`. Supports historical index
membership, entry date, removal date, delisted securities, suspended securities
and historical constituent sets.

The bias being eliminated:

```
today's VN30  →  backtest in 2018                               ✗ survivorship
backtest date →  historical universe →  only what belonged then  ✓
```

Half the problem is already solved: the instrument master never deletes, so a
delisted security keeps its identity and its history. Membership solves the
other half — knowing *when* it belonged.

**Coverage gaps are findings, not silence.** Historical VN30 membership is hard
to source. Where it cannot be established, the gap is recorded as a
data-quality finding so that an empty membership history and a complete one are
never indistinguishable.

**Ingestion follows the universe.** Once membership exists, the ingestion policy
can be driven by a universe rather than by a configured ticker list, which is
also what stops the ingested set and the researched set from drifting apart.

---

## Corporate actions and adjustment (U4)

The Phase 4 shape is correct and does not change:

```
raw data  +  corporate action events  +  versioned adjustment rules
                          ↓
                  adjusted view (on read)
```

`quant.bars` holds what the source printed and keeps holding it. A wrong factor
is one row in a small derived table and one recompute; a rewritten price series
has no way back.

### What U4 changes

The read filters actions by announcement time. The cumulative factor product is
taken over actions with `announced_on <= knownAsOf`, so an action that had not
been announced at the requested instant cannot rescale the series returned for
it.

### Null-announcement policy

Many Vietnamese sources omit the announcement date. That absence is handled
explicitly, never implicitly:

| Mode | Null `announced_on` | Default for |
| --- | --- | --- |
| **Strict** | Excluded | Backtests, dataset export |
| **Permissive** | Included | Charting, terminal display |

Every response states the mode and the `knownAsOf` that produced it. *"We do not
know when this was announced"* must never silently become *"we always knew"*,
and a chart and a backtest may legitimately want opposite answers as long as
both say which they got.

### Action validation

Phase 4 recorded a gap against itself: nothing cross-checks an imported ratio
against the price series, so a ratio transcribed as 20 instead of 2 produces a
plausible factor and a ruined chart. U4 closes it. An action whose implied
factor does not correspond to an observed discontinuity raises a data-quality
finding — reusing the Phase 3 findings machinery rather than inventing a second
mechanism, and recording the suspicion rather than correcting the data.

### Revision history

`corporate_action_revisions` retains what `CorporateAction.Version` already
counts. The version field records that an action was restated; the revision
table records what it said before.

---

## Lineage

Every canonical bar already carries, and continues to carry:

| Field | Answers |
| --- | --- |
| `source` | Which provider produced this |
| `ingested_at_utc` | When it first arrived |
| `revised_at_utc`, `revision` | Whether and how often it has been restated |
| `transformation_version` | Which normalisation rules produced it |
| `validation_version` | Which quality rules have checked it |

A partial index on `validation_version` finds rows written under superseded
rules, so changing a rule is a query for the affected rows rather than a
re-validation of the whole series.

Raw provider payloads are retained verbatim with a checksum, so re-normalising
from source is always possible. Every normaliser has bugs found later, and
without the original payload the only remedy is a re-fetch, which for historical
data is often impossible and never free.

---

## Canonical dataset (U5)

The dataset PQT owns. **No third-party research framework may become the
canonical data model**; frameworks consume this and hand results back.

```
dataset_id · dataset_version · schema_version
known_as_of · adjustment_mode · null_announcement_policy
universe_code · universe_as_of · instrument_set    (canonical IDs, never tickers)
interval · date_range
transformation_version · validation_version
source_set                                         (provider codes + licence note)
row_count · per-file sha256
created_at · created_by_commit
```

Written as Parquet with a JSON manifest governed by a JSON Schema. The manifest
is generated by the backend and validated by the Python layer on load, which is
what keeps the two languages from drifting apart — a concern
[ADR-004](decisions/ADR-004-python-quant-layer.md) raised and left open.

**Hashes are verified on load, not merely recorded.** A dataset whose file no
longer matches its manifest is a hard error, because a reproducible result
computed from a silently changed input is worse than no result.

**Reproducibility property:** the same parameters produce the same manifest
hash. An export at a past `known_as_of` excludes both later revisions and
later-announced actions.

---

## Storage tiers (U7)

| Tier | Technology | Holds |
| --- | --- | --- |
| **System of record** | PostgreSQL 17, `quant` schema | Instruments, bars, revisions, actions, universes, findings |
| **Raw store** | PostgreSQL today, behind a payload-store seam | Verbatim provider payloads |
| **Research store** | Parquet files | Immutable, versioned, hashed datasets |
| **Analytical store** | DuckDB, embedded | Queries over Parquet; no server, no operations |
| **Experiment store** | PostgreSQL, `research` schema | Experiments, runs, metrics, artifacts, jobs |
| **Cache** | Redis | Distributed ingestion lock, manifest cache |

The research store is deliberately files rather than a database. A dataset
version that is a file with a hash can be copied, archived and verified; a
dataset version that is a database state cannot.

**Not adopted, and the trigger that would reverse it.** TimescaleDB and
ClickHouse. Daily bars for roughly 400 Vietnamese tickers across fifteen years
is about 1.5 million rows, which PostgreSQL will not notice. Revisit at
one-minute bars across more than 500 instruments over more than three years —
roughly 150 million rows — or when a range query on `quant.bars` exceeds about a
second. Adding an analytical database before then would be infrastructure
without a workload.

Redis is currently provisioned, health-checked, gating readiness, and used by
nothing. U7 either gives it a real job or removes it from the readiness gate. A
dependency that can fail a deployment while contributing nothing is a liability.

---

## Bias controls, summarised

| Bias | Control | Workstream |
| --- | --- | --- |
| Look-ahead on prices | As-of reads over observation time | U1 |
| Look-ahead on actions | `announced_on <= knownAsOf`, strict mode for research | U4 |
| Look-ahead on fundamentals | Same mechanism, inherited via `reported_at` | Phase 6 |
| Survivorship | Recorded historical membership | U2 |
| Revision and restatement | Append-only observation history | U1 |
| Silent input change | Manifest hash verified on load | U5 |
| Provider coupling | Adapter boundary; canonical schema owns the semantics | U3 |
