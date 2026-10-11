# ADR-025: History rows in the canonical dataset

**Status:** Accepted · **Date:** 2026-10-11 · **Phase:** Research Foundation Upgrade (U5 × U6)

## Context

[ADR-024](ADR-024-point-in-time-dataset-membership.md) made a dataset
point-in-time: a bar is written only where its instrument was a member that
session. That removed survivorship bias, and it removed something else with it.
A name admitted to the universe has no bar before its admission, and no name has
a bar before the window starts. A factor that reads a year of prices cannot
score a newly admitted name for its first year in the universe, and cannot score
anything during the window's first year.

On the real VN30 export the 12-1 momentum slice leaves 2 of 30 members unscored
at every rebalance. Nothing is wrong with the factor: the prices it needs were
printed, a strategy could have read them on the day, and the export left them
out. The fix belongs in the export. Patching each factor to fetch what the
dataset omitted would put a second read path beside the canonical one, which is
what ADR-023 exists to prevent.

Separately, the universe has recorded when each removal was announced
(`UniverseMembership.RemovalAnnouncedOn`) since the removal announcement was
modelled, and the manifest did not carry it. `DatasetSpell` recorded that as a
decision still to be taken, because carrying it changes the manifest and its
hash. A schema change for history is the moment to take it.

## Decision

**An export may carry history: bars a feature may read, which are never
membership.** Membership semantics are unchanged.

`pqt dataset export ... --history N` (default 0, at most 1,000). For each
exported instrument the export also writes:

- up to N bars immediately before its first membership session in the window,
  or for an as-of export before the window starts; and
- with N above zero, every bar between two spells of the same instrument, up to
  its last membership session in the window.

So a series runs continuously from where its history starts to its last
membership session. A row outside a spell (or, for as-of, outside the window) is
a **history row**. Nothing is written after the last membership session: a bar
from after a name left is not history to anything in the dataset.

Five things are decided:

**1. History is read through the same query as everything else.** One bounded
read per instrument with no start and the first membership session as its
exclusive end. The series query takes its bound from the newest end, so this is
exactly "the last N bars before admission", whatever the calendar and holidays
were. Adjustment, `known_as_of` and the announcement policy apply as they do to
every other row.

**2. Membership is still only the spells.** `members_on` reads the manifest's
spells (or the as-of set) and nothing else. A history row is a price a lookback
may read; it never makes an instrument eligible to be scored or held. The
harness's non-member check is unchanged.

**3. The manifest is schema version 3.** Top-level `history_sessions`; per
instrument `history_from` — the first session of its rows when the export
carried history, `null` when it carried none or the instrument has no rows; per
spell `removal_announced_on`, kept when published on or before the window's last
day (a later notice describes what happened after the dataset ends).
[`../../schemas/dataset-manifest-v3.schema.json`](../../schemas/dataset-manifest-v3.schema.json)
is the contract. A reader accepts a row outside the spells or window only when it
is a history row: on or after its instrument's `history_from` and not after its
last spell's end (`to_date` for as-of). It refuses every other such row, a
`history_from` its rows do not begin on, and a `history_from` in a manifest
without history.

**4. The content hash covers the new fields from version 3 only.**
`history_sessions|` follows the rule versions; each instrument renders
`history_from` (or `none`) after its identifier; each spell renders its removal
announcement (or `unannounced`) after the admission's. A version 1 or 2 manifest
renders none of them and hashes exactly as it was written, so every dataset
already on disk keeps verifying. The two languages are held to one committed
fixture, `quant/tests/fixtures/manifests/v3-history.json`, which the .NET test
writes and checks and the Python test hashes.

**5. Existing requests keep their identifiers.** `|history:N` is appended to the
canonical request string only when N is above zero. A history export is a
different dataset from the same window without it — different rows — and is
named differently.

## Alternatives

**Fetch the lookback inside the factor.** Every factor would need its own read
of prices the dataset did not hold, and two factors would disagree about which
prices those were. The dataset is the contract precisely so this cannot happen.

**Write the history as a separate file.** Keeps "rows are membership" true of
`bars.parquet`, and hands every consumer a join between two files with the same
key. ADR-024 rejected the same shape for membership; the manifest already says
which rows are members, so a second file adds nothing a reader cannot derive.

**A `history` flag column on each row.** Changes the Parquet schema every reader
already holds, and duplicates what `history_from` and the spells already
determine.

**Start the window earlier.** Moves the problem rather than fixing it: the
window's own first year is scorable, and a name admitted inside it still is not.
It also widens membership, which is the one thing this must not change.

**Amend ADR-024 instead.** ADR-024 is about who belonged when, and stays true
word for word. History is about which prices travel with the members, and has
its own reasons and costs; recording it separately keeps both readable.

## Consequences

- A history export holds rows a consumer must not read as membership. Every
  reader in this repository goes through `members_on` or the `membership` view,
  and the integrity check refuses any row that is not explicitly history, so a
  forged or misplaced row is still a hard error.
- The momentum slice rebalances only inside the window; with history covering
  its lookback the first rebalance is the window's first session, and a name
  admitted mid-window is scored at its admission.
- `forward_returns` may now value a name across a gap between its spells, since
  the gap's bars are in the file. That is what holding it would have earned; the
  harness still stops it being chosen while it was not a member.
- A `dataset verify` of a version 1 or 2 manifest is unchanged. A new export
  always writes version 3, with `history_sessions: 0` when no history was asked
  for.
- The removal announcement is now part of what a point-in-time dataset is, and
  of its hash.
