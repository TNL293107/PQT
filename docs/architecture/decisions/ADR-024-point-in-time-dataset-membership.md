# ADR-024: Point-in-time membership in the canonical dataset

**Status:** Accepted · **Date:** 2026-09-19 · **Phase:** Research Foundation Upgrade (U2 × U5)

## Context

[ADR-023](ADR-023-canonical-dataset-contract.md) records "the universe and the
date it was read at". An export read one constituent set, on one date, and wrote
every bar each of those instruments had anywhere in the window.

That is correct for a window inside one basket and wrong for any window that
crosses a review. VN30 changed on 3 August 2026: PLX and TPB left, MCH and TCX
joined. An export from July to September read as of 3 August carries MCH and
TCX for July, before they were members, and drops PLX and TPB for July, when
they were. Read as of 1 July instead, it makes the opposite mistake for August.
No single date gives the right answer, because the right answer is a different
basket on different sessions.

This is the survivorship bias that [ADR-020](ADR-020-universe-membership-and-coverage.md)
built the membership history to remove, re-introduced at the last step. The
history was correct; the export discarded it.

## Decision

**A dataset's membership is a declared mode, and point-in-time is the default.**

| `membership` | Which rows are written | `universe_as_of` | Per instrument |
| ------------ | ---------------------- | ---------------- | -------------- |
| `point_in_time` | A bar only where its instrument was a member that session | `null` | `spells` within the window |
| `as_of` | Every bar each member of one set has in the window | the date read | `spells: null` |

Five things are decided:

**1. Point-in-time refuses a window the universe does not know end to end.**
The catalog answers *unknown* unless the declared coverage covers both the first
and the last day. A window starting before the sourced history would otherwise
come back with the names that happened to be sourced later, and a backtest over
it would be survivorship-biased in exactly the years nobody recorded.

**2. The manifest carries the spells, clipped to the window.** Without them a
reader cannot tell a session an instrument was not a member from one it simply
has no bar for, a suspension for instance. Each spell keeps its `announced_on`,
because trading an inclusion before its announcement is look-ahead.

**3. The CLI defaults to point-in-time.** `pqt dataset export --universe VN30
--from … --to …` is survivorship-free. `--as-of <date>` asks for the older
single-set reading by naming the date it is read at. It is chosen, never
defaulted into, for the reason ADR-022 made the dataset's announcement policy
strict: a dataset is read repeatedly by things nobody has written yet.

**4. Existing datasets keep their identity.** An as-of request is named from
exactly the same canonical string as before, so `dataset_id`s already on disk
still mean what they meant. A point-in-time request renders `point-in-time`
where the date would be, and cannot collide with any as-of request. Spells enter
the content hash only when present, so a version 1 manifest verifies as it
always did.

**5. The manifest schema is version 2.** `membership` is required;
`universe_as_of` may be null; each instrument has `spells`.
[`../../schemas/dataset-manifest-v2.schema.json`](../../schemas/dataset-manifest-v2.schema.json)
is the contract new manifests are held to. The version 1 schema stays published
because version 1 manifests stay on disk, and a reader that meets one treats it
as `as_of`.

## Alternatives

**Keep a single as-of date and document the caveat.** The caveat is the bug.
Nobody reads a manifest before running a backtest, and the result looks
plausible either way.

**Write every bar in the window and a separate membership table beside it.**
Complete, and it hands the join to every consumer, each of whom can get the
half-open interval wrong in their own way. A row that is not in the file cannot
be used by mistake.

**A membership flag column on each row.** Doubles the rows a consumer must
filter, and the same mistake is one missing `WHERE` away.

**Keep as-of as the default.** The default is what gets used. It would leave the
biased reading as the one a new research script meets first.

## Consequences

- `pqt dataset export` without `--as-of` now produces a different dataset
  (a different `dataset_id`) from the same command before this change. That is
  intended: it is a different, correct dataset, and the old one is still
  available by naming its date.
- A point-in-time export over VN30 from 2026-07-01 to 2026-09-17 holds 1,620
  rows from 32 instruments: exactly 30 members on each of 54 sessions, none
  outside its spell, with the two leavers and two joiners of 3 August on the
  correct sides of it.
- Instrument labels are still the ticker the master holds at export time
  (`ticker_at_as_of`). Venue and ticker history remain unmodelled; rows are
  keyed by `instrument_id`, which does not change.
