# ADR-013: Data quality rules, the trading calendar and lineage

**Status:** Accepted · **Date:** 2026-08-27 · **Phase:** 3

## Context

Phase 3 is the point at which the project stops having *data* and starts having
data worth running research on. Phase 2 already refuses what a single row can be
shown to be wrong about — a high below its close, a price of zero, a repeated
period — and stores the rest. What it cannot see is anything that needs a second
row or a calendar:

- **A discontinuity.** A close that halves overnight is a split, a bad print, a
  halt or a symbol change. Every one of those is a real event, and none of them
  is visible in the bar itself.
- **An absence.** A day with no bar is a public holiday or a failed ingestion
  run, and without a calendar the two are the same absence.
- **A rule change.** When the checks themselves change, nothing says which rows
  were checked under which version, so the only safe response is to re-validate
  everything — which for a growing series eventually means never changing a rule.

Vietnam offers an unusually sharp test for the first of these. HOSE bands at
±7%, HNX at ±10% and UPCOM at ±15%, and the exchange rejects orders outside the
band, so a larger move did not happen the way the numbers claim.

## Decision

**A finding is recorded; the bar is kept.** Refusing data because it looks wrong
would lose the only record of what the source said, and a hole where a corporate
action happened is worse than a flag on it. What is refused is *silence*: the
discontinuity is written down, and a consumer that cannot tolerate an
unexplained one can see it and stop.

**Three rules, all needing context beyond one row:** `PriceLimitBreach`,
`MissingSession`, `UnexpectedSession`. Structural checks stay domain invariants
and duplicates stay a primary key — neither becomes a finding, because neither
can reach storage.

**One finding per instrument, resolution, session and kind** — a unique index,
including resolved ones. Without it a nightly run raises the same finding every
night and Monday's dismissal is buried by Friday.

**Findings are staged in the caller's transaction, never committed by the
inspector.** That is what lets ingestion store a bar and the finding about it
together. A bar committed without its finding looks clean, and nothing would
know to re-check it.

**Nothing is corrected automatically.** Phase 4 explains price-limit breaches by
matching them to corporate actions. Until then they stay open, which is an
honest description of what the system knows.

**Daily bars only.** A price limit governs a session, so checking a five-minute
bar against it would flag nothing on a day a security moved its full band and
everything on a day it gapped at the open.

**Indices are exempt from the band.** A limit binds orders; an index is
calculated rather than traded.

**A venue with no recorded limit is not checked against one.** Absent is not
zero and not "unlimited"; guessing would either raise false findings or hide
real ones.

**The trading calendar is imported, never seeded.** Tet and the Hung Kings
commemoration are lunar, and substitute days are set by annual decree, so the
Vietnamese calendar cannot be derived.

**Calendar-dependent checks are skipped where the calendar does not cover the
window, and completeness is reported as unmeasured rather than computed.** A
`calendarIsComplete` flag travels with every score.

**Every bar carries two lineage versions:** the normalisation rules that
produced it, and the quality rules that have checked it. A restatement clears
the validation stamp, because the values moved and what the rules concluded
about the old ones no longer applies.

**The score has four components and one weighted summary,** with completeness
weighted heaviest. The components are what a decision rests on; the summary is
for a dashboard, and the counts travel beside both.

## Alternatives

**Rejecting a bar that breaches the band.** Rejected. The bar is usually
correct and the *series* is what has a discontinuity — a corporate action is
real data, and discarding the session would make the gap permanent while
removing the evidence of why.

**Correcting the discontinuity by scaling the prior series.** Rejected here:
that is an adjustment, it belongs in Phase 4 where the corporate action that
justifies it is known, and doing it on suspicion would silently rewrite history
on the strength of a threshold.

**Seeding the fixed-date Vietnamese holidays.** Rejected, and this was the
closest call. Seeding the four statutory dates would make the calendar look
populated, so `IsComplete` would be true, so the whole of Tet would be reported
as missing sessions — several hundred false findings that would bury the real
ones. Reporting completeness as unmeasured is less useful and more honest.

**Deriving trading days from weekdays alone.** Rejected: it is the same failure
with more confidence attached.

**Committing findings separately from the bars.** Rejected. It works, and the
validation version even makes it self-healing, but it admits a window in which a
bar exists with no finding beside it and nothing knows to look.

**A single quality number.** Rejected as the primary output. A series that is
99% complete and 99% consistent is not interchangeable with one that is 100%
complete and 98% consistent, and one number cannot tell them apart.

**Making `Ratio(0, 0)` zero rather than one.** Rejected: it would report a
series nobody has ingested yet as maximally broken. The counts beside the score
keep "nothing known to be wrong" distinguishable from "nothing wrong".

## Reasoning

The pattern is the one the earlier ADRs set, applied to a harder case: prefer
the loud, recorded refusal to the plausible number. What is new in this phase is
that some of the refusals are about *this system's own knowledge* rather than
about the data — an unmeasured completeness figure and an unvalidated bar are
both admissions, and both are more useful than the confident answer they
replace.

The price limit is worth the specificity. A generic ±30% heuristic would catch
splits and miss a mis-scaled feed; the venue's own band catches anything the
exchange would have rejected, which is exactly the set of moves that cannot have
happened as printed.

## Trade-offs

- **A calendar has to be sourced before completeness means anything.** Deliberate,
  and the flag makes it visible rather than letting a wrong number circulate.
- **The band's tolerance is a judgement.** Half a per cent absorbs tick rounding
  and is far below any corporate action, but it is a number chosen rather than
  derived.
- **Findings are per session, so a month-long feed fault raises twenty of them.**
  Correct but noisy; grouping them into an incident is a reporting concern that
  can be added without changing what is stored.
- **The score's weights are a judgement too.** Documented as constants with
  reasons, and the components are published so a consumer can ignore the
  aggregate entirely.
- **Lineage costs two columns on the largest table in the system.** Cheap
  relative to what it buys: a rule change becomes a query rather than a
  re-validation of everything.
- **Intraday series get no quality checks at all.** Honest — the session-scoped
  rules do not apply — but it means an intraday series is unmeasured rather than
  measured as good.

## Consequences

- Phase 4 has a queue to work from: the open `PriceLimitBreach` findings are
  the candidate corporate actions, and explaining one is a recorded resolution
  rather than an edit.
- Phase 9's backtester can refuse to run over a window holding unexplained
  discontinuities, which is the guarantee this phase exists to provide.
- Changing a validation rule means bumping `DataRules.ValidationVersion`; the
  bars needing re-checking are then found by an indexed query.
- A deployment that has imported no calendar gets findings for discontinuities
  and no findings for absences, and the score says so.
