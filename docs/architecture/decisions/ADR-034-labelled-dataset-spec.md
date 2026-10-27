# ADR-034: The labelled-dataset specification — a request recorded in the manifest (v5)

**Status:** Accepted (Lead, 2026-10-27, after QA + QC review: 2 blockers and 8 minor points, all applied) · **Date:** 2026-10-27 · **Phase:** Sprint 9, items 9.1 and 9.2 (Phase 7)

## Context

U5 defines the canonical dataset, U6 the feature and factor protocols, U9 the
run record, and ADR-033 the feature catalogue. Nothing yet says, in one
declarative object, *which* supervised research dataset a factor report ran on:
universe and as-of, window, rebalance frequency, the features with their
versions, and the forward-return label. Today that is spread over CLI flags
(`pqt dataset export`), worker parameters and code defaults
(`rebalance_every=21` in `momentum_slice.py`).

The roadmap (Phase 7) and the sprint plan (9.2) fix two constraints: the spec
**resolves to a manifest extension (v5)**, and it is **not a parallel dataset
concept** — there is one canonical dataset contract, and this is a way of asking
for one.

The window is a stated limit (9.1, Moderator 2026-10-21): VN30 membership from
2024-08-05, bars from 2023-07-03, so about 2024-08 → now, roughly 26 rebalances
of 21 sessions at most 30 names. It is not widened in sprint 9.

## Decision

**A labelled dataset is a canonical dataset whose manifest carries the request
that produced it. Features and labels are declared there and computed at read
time by the code the request names; they are not stored.**

### 1. The request

A JSON file, `labelled-dataset-request-v1`:

```json
{
  "universe": "VN30",
  "as_of": null,
  "from": "2024-08-05",
  "to": "2026-10-23",
  "interval": "1d",
  "fundamentals": true,
  "rebalance_every": 21,
  "features": [
    {"name": "momentum_12_1", "version": 1},
    {"name": "size", "version": 1}
  ],
  "label": {"horizon": 21, "basis": "adjusted"}
}
```

- `as_of` must be `null` in v1: point-in-time membership, the survivorship-free
  reading. The export is always adjusted. Both are pinned by the request, so a
  raw or as-of build cannot carry one (QA + QC).
- `from` is the **first scored session**, not the first bar. The history a
  feature needs is derived, not asked for (§2).
- Rebalances fall on the first session ≥ `from` and every `rebalance_every`
  sessions after it. History is sized from the lookbacks, so adding a feature
  does not shift the grid.
- `label` is the forward return from the close of the rebalance session *t* to
  the close of *t + horizon*, valued as `forward_returns` values it: a name that
  leaves the universe inside the period is valued at its last member close.
  In v1 `horizon` must equal `rebalance_every`, so labels neither overlap
  (autocorrelated IC) nor leave gaps. Other horizons are sprint 10's decay,
  computed over the same build.
- `basis` is `adjusted` only in v1 (see Alternatives).

### 2. Resolution

`pqt dataset export --request <file>` resolves the request into the existing
export parameters and nothing else:

| Request | Export |
| --- | --- |
| `universe`, `interval`, `fundamentals` | the same options |
| `as_of` | null only (v1): point-in-time |
| `from`, `to` | `--from`, `--to` |
| features | `--history` = the largest declared lookback − 1 |

The export refuses the request while resolving it if `as_of` is not null,
`horizon` differs from `rebalance_every`, or `basis` is not `adjusted`. The v1
rules are enforced there, before anything is written.

The backend does not know what a feature computes; it receives the lookbacks
from the request, which carries them beside name and version (written by the
Python tool that builds the request from the registry). The Python reader
checks them against the registry at read time (§4), so a request that lies
about a lookback is refused, not trusted.

### 3. Manifest v5

Schema 5 is schema 4 plus one optional object, `request`: the request as
resolved, with each feature's `lookback` filled in. It enters the content hash
from schema 5 only, the way history entered at 3 and fundamentals at 4, so
schema 1–4 manifests hash exactly as written.

The rendering, appended right after the fundamentals part and before the
instruments, identical in C# and Python:

```
request:{universe},{from},{to},{interval},{True|False},{rebalance_every},{horizon},{basis};{name}:{version}:{lookback},...|
```

Dates are ISO `yyyy-MM-dd`, integers invariant-culture, the boolean is the
fundamentals flag in .NET's spelling, and features are **sorted by name**
by **ordinal** comparison (C# `StringComparer.Ordinal`, Python `sorted()`),
whatever order the file holds. With no request the part is `request:none|`.

The dataset id is derived from the export parameters as today. **Only when a
request is present** is its rendering appended to the id input, as
`DatasetIdentity.Of` appends history and fundamentals. The same request names
the same dataset, a different feature list or label is a different dataset,
and an export without a request keeps the id it had.

### 4. What the reader checks

`DatasetManifest.from_json` accepts schema 5. `LabelledDataset.open(...)`
verifies the build (files and content hash, as today) and refuses it, with the
reason, when:

- the manifest has no `request`;
- a declared feature is not in the registry, or its version or lookback differs
  from the registry's.

Nothing else needs checking. History and fundamentals were exported from the
same request, so they cannot disagree with it without breaking the hash. The
value features already refuse `ratio_version` < 2 themselves (ADR-033 §2).

Rebalances whose *t + horizon* passes the last session are dropped, not
refused, and the count that remains is stated (§6).

It then yields, per rebalance session, the feature cross-sections (through the
point-in-time harness, as today) and the label mapping (outside the harness, as
`forward_returns` is today). Absent stays absent in both.

### 5. What a run records

A factor run (sprint 10) records the dataset id, version and content hash, as
U9 already does. Because the request is inside the hashed manifest, those pin
the universe, window, frequency, features with versions and label. The run's
`parameters` hold only what is not in the request (quantiles, transforms).

### 6. The window statement (9.1)

The reader exposes the window it produced: first and last scored session,
number of rebalances, names per rebalance (min, median, max). Every factor
report prints it with ADR-033 §5's sentence: at most about 26 monthly
cross-sections of at most 30 names, exploratory.

## Alternatives

- **Materialise `features.parquet` and `labels.parquet` in the export.** The
  backend would need a second implementation of every feature, or Python would
  write into a .NET-hashed build. Rejected: features are computed once, in
  `personal_quant.factors`, and the hash already pins their inputs.
- **A separate "labelled dataset" record beside the canonical one.** The
  parallel concept the roadmap rules out. Rejected.
- **Spec only in the run's `parameters`.** Reproducible, but two runs on the
  same request are not visibly the same dataset, and nothing checks the spec
  against what was exported. Rejected.
- **Raw-close labels.** A raw close-to-close return across an ex-date counts
  the dividend or bonus as a loss. Refused in v1; a total-return label is a
  later version if a report needs one.
- **Calendar-month rebalancing.** The slice and the harness count sessions;
  mixing calendars adds a rule nobody asked for. Kept in sessions.

## Trade-offs

- **Computed at read time.** A large catalogue is recomputed on every read. At
  ≤ 30 names × ≤ 26 rebalances that is cheap; caching is added when it is not.
- **Lookbacks travel in the request.** The backend trusts them to size
  `--history`; the reader re-checks them against the registry, so a mismatch is
  caught at read, not at export.
- **One label per dataset.** Decay over horizons (sprint 10) reads several
  horizons; that is the evaluation varying `horizon` over `forward_returns`
  on the same build, recorded as trials, not several datasets.

## Consequences

- Backend: `DatasetManifest.CurrentSchemaVersion` = 5; `--request` on
  `pqt dataset export`; the request in the content hash from schema 5;
  `dataset verify` reads v5. New schema file
  `docs/schemas/dataset-manifest-v5.schema.json`.
- Python: `SUPPORTED_SCHEMA_VERSIONS` gains 5; `content_hash_of` renders the
  request exactly as the C# does; `LabelledDataset` in
  `personal_quant.research`; a small builder that writes a request from the
  registry.
- After the bump, every export is schema 5. Re-exporting a v4 build's
  parameters gives the same dataset id and a new version with a different
  content hash, because the schema number and `request:none|` are in it. The
  v4 build itself still verifies; reproducing a v4 run means reading that
  build, not re-exporting it.
- Tests: a v4 manifest still verifies unchanged; an edited request breaks the
  hash; each refusal in §4 has a test; a hand-built request on the synthetic
  dataset yields the expected rebalance sessions and labels.
- Sprint 10's evaluation reads a `LabelledDataset`, not raw CLI flags.
