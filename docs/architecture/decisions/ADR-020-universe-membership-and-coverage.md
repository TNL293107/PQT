# ADR-020: Universe membership and the coverage claim

**Status:** Accepted · **Date:** 2026-08-30 · **Phase:** Research Foundation Upgrade (U2)

## Context

Selecting today's VN30 and running a strategy over 2018 picks the thirty
securities that survived to today. It is the cleanest example of survivorship
bias there is, and it produces a backtest of a portfolio nobody could have
held — one that looks better than reality, with nothing in the numbers saying
so.

Half the problem is already solved. The instrument master never deletes, so a
delisted security keeps its canonical identity and its price history
([ADR-009](ADR-009-instrument-identity-and-ticker-lifecycle.md)). What it
cannot say is *when* a security belonged to the set a strategy was choosing
from, because nothing records membership at all.

The other half is harder than it looks, and it is not the schema. Vietnamese
index membership is published in review notices rather than served on an
endpoint, and any history of it will be partial for years before it is
complete. A model that records membership without recording *how much of it is
recorded* would answer a query about 2018 with an empty set — indistinguishable
from an index that genuinely had no constituents. The bias would move from the
data into the silence around it.

## Decision

Three tables, and one distinction that is not in the rows.

```
quant.universes
  id, code, name, kind, source
  coverage_from, coverage_until          -- the claim; both null = claims nothing

quant.universe_memberships               -- append-only
  universe_id, instrument_id, effective_from   (pk)
  effective_to NULL                      -- exclusive; NULL = still a member
  announced_on, source, recorded_at_utc

quant.universe_coverage_findings
  id, universe_id, kind, detail, detected_at_utc, status, …
```

### Decision 1 — Membership is half-open and append-only

`[effective_from, effective_to)`, matching the observation window of
[ADR-018](ADR-018-point-in-time-market-bars.md). A review removes one name and
admits another on a single date, and only a half-open interval puts that date
on exactly one side of each; inclusive bounds make an index of thirty briefly
hold thirty-one.

A row is never updated except to close its interval, and never deleted. **Re-
entry is a second row**: a security demoted at one review and restored at a
later one has two disjoint spells, and the gap between them is exactly what a
survivorship-free backtest must be able to see.

### Decision 2 — Overlap is refused by the schema, not by the importer

```sql
EXCLUDE USING gist (
    universe_id   WITH =,
    instrument_id WITH =,
    daterange(effective_from, effective_to, '[)') WITH &&)
```

The primary key makes spells distinct by start date, which is what permits
re-entry. What it cannot see is two spells of one security covering the same
dates — a second import run recording a spell nobody closed, or two sources
disagreeing. Both make an index's constituent count silently wrong, and neither
is visible in any single row.

`btree_gist` supplies the equality operator classes for `uuid`. It is a trusted
extension on PostgreSQL 13 and later, so a database owner installs it without
superuser, and it is not dropped on the way down: another object may come to
depend on it.

A check constraint refuses an interval covering no session
(`effective_to > effective_from`), which the domain refuses too. The constraint
is what makes the refusal true of the table rather than of one code path.

### Decision 3 — The coverage claim is stored, not derived

A universe states the span whose membership it claims to know, separately from
its rows. An as-of read outside that span answers **unknown**; inside it, an
empty result is a fact about the market.

`MIN(effective_from)` was rejected as a derivation: a history sourced with a
hole in the middle would look continuously known. Nothing in the rows can
distinguish *an index had no constituents then* from *nobody sourced then*, and
that is precisely the distinction the claim exists to carry.

### Decision 4 — An unknown answer has no member list

`UniverseConstituents.Members` throws when the membership is not known, rather
than returning an empty list. The survivorship bug takes exactly the shape of an
empty list nobody checked, and a caller that forgets the check must fail loudly
on its first run rather than produce a backtest over an empty market.

### Decision 5 — Coverage gaps are findings, in a table of their own

Three kinds: no membership recorded at all, rows with no claim, and rows
outside the claim. One open finding per universe and kind, enforced by a
partial unique index, so a nightly review cannot stack duplicates and a
dismissal survives.

`quant.data_quality_issues` was rejected as the home. It is keyed by instrument,
resolution and session; a coverage gap concerns a set, on no particular day, for
no particular security. Making three columns nullable to fit would weaken the
invariants that make a bar finding trustworthy, in order to store something that
is not one.

### Decision 6 — `announced_on` is recorded and not read

An index review is published before it takes effect, so a strategy acting on
the announcement before its publication date is looking ahead. Filtering on it
is [U4](../../roadmap/pqt-roadmap-v2.md)'s work, and the column is collected now
so the history does not have to be re-sourced then. This mirrors
`corporate_actions.announced_on`, which is in the same state for the same
reason.

## Alternatives

**Membership as a column on the instrument** (`is_vn30`). Rejected outright: it
has no time in it, which makes it a statement about today that silently answers
questions about every other year — the bias itself, in one boolean.

**A snapshot per review date** — the full constituent set stored for each
review. Simple to query and wrong to maintain: it stores O(reviews × members)
rows to express O(changes) facts, and a correction to one spell means rewriting
every snapshot after it.

**Deriving the universe from the instrument master's listing lifecycle.** Works
for an exchange universe and not for an index, which is the case that matters:
index membership is a decision made by the index owner, not a consequence of
being listed.

**Letting an unsourced date return an empty set, and documenting it.** Rejected
because documentation is not a constraint. Every consumer would have to
remember, and the failure is silent in the one direction nobody checks.

## Reasoning

The half-open interval and the append-only table are the same shape U1 chose
for observation time, and using one shape for both means one rule to know:
`from <= x < to`, everywhere, for every interval in the system.

The coverage claim is the part that is genuinely new, and it is there because
this workstream's honest state for the next several months is *partial*.
Membership for the recent past can be transcribed; 2018 may never be. A model
that cannot express partial knowledge would force a choice between fabricating
the missing years and leaving them looking empty, and both of those are the
bias in a different coat.

## Trade-offs

- The coverage claim is a human assertion, so it can be wrong. It is recorded with its source, and a claim that disagrees with the rows raises a finding — but nothing can verify that a file said to contain every review from 2024 actually does.
- Two findings tables now exist. They share a status vocabulary and no code, and a dashboard eventually has to read both.
- `Members` throwing means an unchecked caller fails at runtime rather than at compile time. A result type the compiler could enforce was considered and rejected as more machinery than one property and a test.
- A read costs two queries — the universe, then its constituents — because the claim is consulted before the rows. Universes are few and the first query hits a unique index.
- Repository reads fold in what the current unit of work has staged. That makes a review inside an import correct and makes those reads slightly less obvious than a plain query.

## Consequences

- `GET`-side reads answer through `IUniverseCatalog`, which returns constituents **or** a stated reason for not knowing. There is no path that returns an empty list for an unsourced date.
- The universe import runs last at start-up, after instruments: a membership row names a security by provider symbol, and resolution needs the alias the instrument import wrote. There is no fallback to a bare ticker.
- The import and the coverage review commit in one transaction, so a universe and the record of what is missing from it are never separated.
- **No VN30 history is seeded.** The fixtures are synthetic and state no real index membership. Seeding today's constituents as a stand-in for earlier years would commit the bias this ADR removes.
- **U3 does not wait for this.** Ingesting one real ticker needs no membership history; the ingestion policy may be driven by a universe once one exists, and that is an option rather than a prerequisite.
- Nothing here touches `quant.bars`, `bar_revisions`, corporate actions, the dataset contract, the Python layer or Redis.
