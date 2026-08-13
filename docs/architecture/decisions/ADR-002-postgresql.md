# ADR-002: PostgreSQL as the system of record

**Status:** Accepted · **Date:** 2026-08-13 · **Phase:** 0

## Context

The terminal will store two shapes of data with very different demands:

- **Reference and transactional data** — instruments, identifiers, portfolios,
  orders, fills. Relational, heavily joined, and correctness-critical. An order
  or a position that is wrong is worse than one that is slow.
- **Time series** — OHLCV bars, trades, quotes. Append-heavy, queried by
  instrument and time range, and eventually large.

Phase 0 needs neither schema. It needs the storage decision made, because
migration tooling, connection handling and the health check all follow from it.

## Decision

PostgreSQL 17 as the single system of record for both shapes, accessed through
EF Core with the Npgsql provider.

Application tables live in a dedicated `quant` schema rather than `public`.

## Alternatives

**SQLite.** Zero setup, a single file, entirely adequate for one user.

**PostgreSQL + a dedicated time-series database** (InfluxDB, ClickHouse) from
the start.

**TimescaleDB** (PostgreSQL extension) from the start.

**A document store** (MongoDB) for provider payload flexibility.

## Reasoning

SQLite is genuinely tempting for a single-operator system, and its correctness
record is excellent. It was rejected on concurrency and type strictness: the
quant layer, the backend and eventually an ingestion process will all want the
database at once, and SQLite's writer lock plus loose typing are poor
foundations for financial data.

Running a separate time-series database from day one means two systems to
operate, two consistency models, and — the real problem — cross-store joins
between prices and instruments done in application code. PostgreSQL handles the
projected volume comfortably: a decade of daily bars for a few thousand
instruments is single-digit millions of rows, which is unremarkable. Intraday
tick data would change that calculus, and that is the trigger to revisit.

TimescaleDB is the natural escalation and remains available precisely *because*
the choice is PostgreSQL — it is an extension, not a migration. Adopting it now
would add an operational dependency to solve a problem the system does not yet
have.

A document store was rejected because the core model is relational. Instruments
join to identifiers, portfolios to positions, orders to fills. Provider payload
variability is real, but it is handled by `jsonb` columns in a relational
model, not by abandoning relational integrity.

The `quant` schema keeps application tables away from extensions and ad-hoc
analysis tables in `public`, so a migration diff never has to reason about
objects it does not own.

## Trade-offs

- A running server is required. Docker Compose covers this; there is no
  file-and-go mode.
- Not a purpose-built time-series store. Partitioning and BRIN indexes will be
  needed before columnar compression would be.
- EF Core adds abstraction over SQL. Mitigated by keeping raw SQL available for
  the query shapes where it matters.

## Consequences

- The connection is configured entirely from `POSTGRES_*` environment
  variables, never from a committed file.
- Connection strings are built with `NpgsqlConnectionStringBuilder`, so no
  value is ever concatenated into one.
- Schema is owned by EF Core migrations. The Phase 0 baseline creates the
  `quant` schema and the migrations history table and nothing else.
- The readiness probe executes a real round-trip query rather than inspecting
  pool state.
- Revisit when intraday tick storage arrives (Phase 2+): first partitioning,
  then TimescaleDB, then a separate store — in that order.
