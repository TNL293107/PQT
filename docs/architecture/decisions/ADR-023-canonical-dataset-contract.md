# ADR-023: The canonical dataset contract

**Status:** Accepted, amended by [ADR-024](ADR-024-point-in-time-dataset-membership.md) · **Date:** 2026-09-06 · **Phase:** Research Foundation Upgrade (U5)

> ADR-024 makes membership a declared mode. A dataset is point-in-time by
> default, reading membership on every session rather than on one date, and the
> manifest is schema version 2. What follows describes version 1, which still
> governs the as-of mode.

## Context

Research runs against a dataset. The question U5 answers is who owns the
definition of that dataset.

The default answer is whichever framework loads it. Qlib has a data model; so
does OpenBB; so does whatever comes next. Adopt one and its shape becomes the
shape of every experiment, every stored result, and every comparison between
them — and swapping it later means re-deriving results nobody can reproduce,
because the inputs were only ever described in the vocabulary of a library.

[ADR-016](ADR-016-python-dotnet-research-boundary.md) put the research layer in
Python and the system of record in .NET. [ADR-004](ADR-004-python-quant-layer.md)
raised the obvious hazard and left it open: two languages describing the same
data will drift, and the drift is silent because both sides go on working.

There is also a narrower problem. A result is only reproducible if every input
it depended on can be named. A series read today and the same series read after
a provider restated it are different data; so are the same bars adjusted under
a strict and a permissive announcement policy; so are the same bars normalised
under two versions of the transformation rules. None of that is visible in a
table of prices.

## Decision

**PQT defines the dataset. Frameworks consume it and hand results back.**

An export writes Parquet plus a JSON manifest into a configured directory, one
directory per build:

```
{Datasets:Directory}/{dataset_id}/v{version}/
    bars.parquet
    manifest.json
```

The manifest names everything a result depends on — the universe and the date
it was read at, the window, the resolution, whether prices were adjusted, the
announcement policy, the observation instant, all three rule versions, every
contributing source, and a `sha256` per file. It is governed by a published
JSON Schema in [`../schemas/dataset-manifest-v1.schema.json`](../schemas/dataset-manifest-v1.schema.json).

Six things are decided rather than merely implemented:

**1. `dataset_id` is derived, never issued.** It is a digest of the export
parameters, so the same request names the same dataset. A random identifier
would make "have we already built this?" unanswerable without comparing every
field by hand.

**2. `content_hash` excludes `dataset_version`, `created_at_utc` and
`created_by_commit`.** Those three say *which run this was*; everything else
says *what the data is*. That separation is what makes the reproducibility
property real: two exports of identical parameters over identical data agree on
the content hash and differ in their manifest bytes, because one of them
happened later.

**3. Hashes are verified, not merely recorded.** `pqt dataset verify` recomputes
every file digest *and* the manifest's own content hash. Checking only the files
would let somebody change the as-of a dataset claims and leave every hash
matching.

**4. Rows are keyed by `instrument_id`; the ticker lives only in the manifest.**
A ticker changes on an exchange transfer and is reassigned after a delisting, so
a dataset joined on one silently mixes two companies. The label a human needs is
recorded once, stamped with the instant it was valid.

**5. Prices are written as strings.** A decimal price forced through binary
floating point is no longer the number that was stored, and every later
reconciliation compares two things that are not equal. Parquet's decimal type
would preserve the value but pins a precision and scale into the file, and an
adjusted price can carry a far deeper scale than any real one.

**6. Every source carries a licence note, defaulting to the true position.**
None of the Vietnamese sources this system reads grants a licence. The data is
retained because nothing prohibits retaining it — an absence, not a permission —
and that does not extend to redistribution. A dataset leaving this system with
no statement attached is one somebody eventually redistributes.

The export defaults to `AnnouncementPolicy.Strict`, the opposite of the chart's
default, per [ADR-022](ADR-022-announcement-aware-adjustment.md). A dataset is
run against repeatedly by things nobody has written yet, so look-ahead baked
into one propagates into every experiment that reads it.

An unknown universe membership **refuses the export**. That is the refusal
[ADR-020](ADR-020-universe-membership-and-coverage.md) exists to make possible:
an empty dataset would produce a backtest that reports no positions and no
error.

## Alternatives

**Adopt Qlib's or OpenBB's data model as canonical.** Free, and it hands the
definition of every future result to a dependency. The moment that library
changes its shape, so does the meaning of everything already stored.

**A database table instead of files.** A dataset version that is a file with a
hash can be copied, archived and verified anywhere. A dataset version that is a
database state cannot be handed to anybody, and cannot be shown to be the same
one a result was computed from.

**CSV.** Readable, and it loses types, costs an order of magnitude in size, and
has no schema. Parquet is what the readers on the other side of the boundary —
pandas, Polars, DuckDB — already speak.

**Write prices as `double`.** Smaller and faster to read, and wrong. The whole
point of storing what the market printed is that it is exactly what printed.

**Skip the JSON Schema and let the manifest be whatever the record serialises
to.** That is precisely the drift ADR-004 warned about. A schema nothing checks
is a document, so a test holds the written manifest and the schema to each other
and fails when either moves.

**Version datasets by timestamp rather than an integer.** Two exports in the
same second would collide, and an operator cannot tell at a glance which of two
timestamps is the later build.

## Reasoning

The contract is small on purpose. It records what a result depended on and where
the bytes are; it does not attempt to describe features, factors, or models —
those are U6's, and a contract that tried to cover them would be obsolete before
the first experiment ran.

Everything in it earns its place by answering a question that cannot be answered
afterwards. *Which rules produced these rows?* — the three versions. *What did
the system believe when it read them?* — the as-of. *Could a strategy have known
about that split?* — the policy. *Who was in the index then?* — the universe and
its as-of. *May I share this?* — the licence notes. Not one of those is
recoverable from a table of prices.

## Trade-offs

**The whole dataset is held in memory before it is written.** Roughly 1.5
million daily rows for the entire Vietnamese market over fifteen years, which is
tens of megabytes. Streaming would complicate the row ordering that makes the
file's hash deterministic. Revisit at intraday resolutions.

**Rows are sorted before writing.** Necessary — otherwise two exports of
identical data hash differently because the database returned them in a
different order — and it costs a sort of the whole set.

**One file per dataset.** The manifest carries a list, so more can be added
without a schema change, but nothing partitions by instrument or year yet.

**Text prices cost space and a parse on read.** Parquet's dictionary encoding
absorbs most of the size. The parse is the price of exactness.

**`created_by_commit` is usually null.** The container build does not set
`SourceRevisionId`. Null rather than a placeholder: a dataset claiming a commit
it does not know is worse than one that admits it does not.

## Consequences

- `Parquet.Net` is a dependency of the Infrastructure project. Fully managed, so
  the container image needs no native library.
- `Datasets:Directory` is configured per deployment and backed by a **named
  volume**, never a bind mount into the repository. An export holds real market
  prices, and the licence position forbids redistributing them; a named volume
  cannot be committed by accident. `data/datasets/` is gitignored for the same
  reason.
- The image creates `/app/datasets` owned by the app user, so a fresh named
  volume is writable by the non-root process.
- `pqt dataset export` and `pqt dataset verify` exist. The export is an operator
  command and not an endpoint: it walks a whole universe across a date range and
  writes to the deployment's own disk, which is a scheduled job's shape.
- U6's protocols consume this contract. A `Dataset` protocol that defined its
  own shape would re-open exactly what this closes.
