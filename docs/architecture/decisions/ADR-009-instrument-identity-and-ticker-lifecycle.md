# ADR-009: Instrument identity and ticker lifecycle

**Status:** Accepted · **Date:** 2026-08-14 · **Phase:** 1 (workstream 1)

## Context

The instrument master is the join key for every later domain: prices,
fundamentals, corporate actions, positions and orders all reference an
instrument. Getting identity wrong is not a bug that shows up as an error — it
shows up as a backtest that silently blends two companies' histories.

Vietnamese markets make the naive choice actively dangerous:

- A ticker changes when an issuer transfers between UPCOM, HNX and HOSE, which
  is a normal progression rather than an exception.
- A delisted ticker is released and can later be reassigned to an unrelated
  issuer.
- FIGI coverage is inconsistent, CUSIP does not apply, and ISIN exists but is
  rarely exposed by providers ([ADR-008](ADR-008-vietnam-market-first.md)).

So there is no external identifier that is both available and stable.

## Decision

**Identity is a surrogate internal `InstrumentId`, issued by this system.**
Ticker, exchange and name are mutable attributes of an instrument, not identity.

**Ticker uniqueness is enforced per exchange, over active instruments only** —
a partial unique index filtered on `status <> Delisted`.

**Instruments are never deleted.** Delisting is a terminal lifecycle state.
The repository exposes no delete operation, and the foreign key from
instrument to exchange is `RESTRICT`.

The lifecycle is a closed state machine:

```
Pending ──► Listed ⇄ Suspended
               │         │
               └────►────┴────► Delisted (terminal)
```

## Alternatives

**Ticker as the primary key**, or a composite `(exchange, ticker)` key.

**An external identifier — ISIN or FIGI — as the key.**

**An absolute unique index on `(exchange, ticker)`**, with no filter.

**Soft delete via an `is_deleted` flag**, alongside the lifecycle status.

## Reasoning

A ticker key fails on the two most common Vietnamese events. On an exchange
transfer it would require rewriting every referencing row, and on ticker reuse
it would silently attach a new issuer's prices to the previous issuer's
history. A composite key with exchange only narrows the second failure; it does
not remove it, because reuse happens on the same exchange.

An external identifier is unavailable often enough to be unusable as a key, and
would make the model depend on provider coverage. Where an ISIN is known it is
stored as an alias.

An absolute unique index looks safer than a filtered one and is in fact wrong:
it would reject a legitimate reissue of a released ticker, forcing an operator
to either mutate the delisted record or invent a fake ticker. Both destroy
history. The filter encodes the actual market rule — *one active holder at a
time* — while allowing the full sequence of holders to be retained and audited.

Soft delete was rejected as a redundant second truth. `Delisted` already means
"no longer trading", and a separate flag creates states where the two disagree.

## Trade-offs

- Every lookup by ticker needs an exchange and a point of view about time.
  `FindActiveByTickerAsync` and `ListTickerHistoryAsync` are separate
  operations precisely so the caller has to choose.
- Two indexes over the same columns: the partial unique one, and a plain one
  serving history queries that must include delisted rows.
- A surrogate key means an extra resolution step for anyone reading raw data
  who only knows a ticker.
- The state machine rejects transitions a provider feed might legitimately
  send out of order. That surfaces as an error rather than as corrupted data,
  which is the intended trade.

## Consequences

- The canonical ID is a UUIDv7. Time-ordered so the primary key of an
  append-only table does not fragment; tested in big-endian byte order,
  because that is how PostgreSQL orders the `uuid` type.
- Audit timestamps are supplied by the caller, not read from a clock inside the
  entity. The domain assembly keeps zero dependencies and every transition is
  deterministic under test. UTC is enforced — a local-time audit stamp looks
  authoritative and shifts when the process moves between machines.
- **Tables and columns are snake_case**, configured per property rather than
  by a global convention. The Python quant layer queries these tables directly
  and PascalCase identifiers in PostgreSQL must be double-quoted at every call
  site.

  `EFCore.NamingConventions` was tried first and reverted. A global convention
  also renames EF's migrations history columns to `migration_id` and
  `product_version`. EF reads that table *before* it can apply any migration,
  so every database created earlier is stranded: it holds `MigrationId` and
  there is no migration that can fix it. The failure does not appear in tests
  that start from an empty database — it only appears on upgrade. Explicit
  per-property naming costs some boilerplate and is guarded by a test that
  fails if any column is not snake_case, plus one asserting the history table
  keeps its default names.
- **`InvariantGlobalization` is now `false`.** Phase 0 enabled it for
  determinism before anything needed a locale; invariant mode also strips the
  IANA time zone database, so `Asia/Ho_Chi_Minh` cannot resolve. An exchange
  must record the zone its trading day is measured in, because a session
  boundary is not derivable from a UTC instant. Determinism is preserved by
  ordinal comparison and invariant casing at each call site instead.
- Exchange reference data is **not** seeded. Real venue attributes are
  provider-sourced facts and belong to the import workstream, not to a
  hand-written migration.
- Later workstreams inherit the rule that a provider symbol is never identity.
  Mapping `FPT`, `FPT.HM` and `FPT:VN` onto one instrument is alias resolution
  over this model, not a change to it.
