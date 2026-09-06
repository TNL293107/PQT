# ADR-022: Announcement-aware adjustment

**Status:** Accepted · **Date:** 2026-09-06 · **Phase:** Research Foundation Upgrade (U4)

## Context

[ADR-018](ADR-018-point-in-time-market-bars.md) made the *prices* point-in-time.
`bar_revisions` records what this system believed at each instant, and a read
with `knownAsOf` returns the statement held then rather than today's correction
of it.

It said so explicitly, and left the other half open: the corporate actions
applied to those prices were not filtered by anything. A series read as of
2016-05-27 was rescaled by every factor in `price_adjustments`, including
factors from actions announced years later. The prices were point-in-time; the
adjustment was not.

That is look-ahead, and it is the worst-behaved kind. It does not produce an
error, a gap, or an implausible number. It produces a series that is smooth,
plausible, and better than reality — and a backtest run over it reports a
result nobody has any reason to question.

`corporate_actions.announced_on` has existed since Phase 4, recorded precisely
so this could be closed later. It is nullable, because most Vietnamese action
records carry an ex-date and nothing else.

That nullability is the actual decision. "We do not know when this was
announced" has two defensible readings and they are opposites:

- Exclude the action. The series under-adjusts, and under-adjustment shows up
  as a visible discontinuity somebody will investigate.
- Include the action. The series is right whenever the announcement did precede
  the read, and looks ahead whenever it did not.

Phase 4 also recorded a gap against itself: nothing checked whether an action's
implied factor corresponded to anything the prices actually did. A ratio
transcribed as 2 instead of 1.2, a dividend entered in đồng where the source
meant thousands, an ex-date off by a week — each rescales a decade of history
for an event that left no trace, and the adjustment is what makes the result
look smooth.

## Decision

**The cumulative factor is taken over actions with `announced_on <= knownAsOf`,
and the null policy is a parameter of the read rather than a convention.**

```
AnnouncementPolicy.Strict       null announced_on → excluded
AnnouncementPolicy.Permissive   null announced_on → included
```

Three rules govern it:

1. **The policy applies only when `knownAsOf` is given.** A read of the current
   series applies everything this system currently knows, which is what
   "current" means. A strict *current* read that withheld actions for want of a
   date would report a series no instant justifies.

2. **The comparison is by day and inclusive.** An action announced on the
   observation date was public that date, and excluding it would misstate what
   a strategy trading that session could read.

3. **Every response states the policy, the instant, and the counts.**
   `announcementPolicy`, `knownAsOf`, `adjustmentsApplied` and
   `adjustmentsWithheld` are on the wire. A caller must be able to see that
   factors were held back and why.

The default is **permissive**, because the reachable callers are the chart and
the terminal. Strict is what a backtest and the U5 dataset export ask for
explicitly — and asking explicitly is the point.

`announced_on` is **copied onto `price_adjustments`** rather than joined on
every read, and `PriceAdjustment.IsCurrentFor` compares it. `Schedule` does not
bump the action's version, so without that comparison a factor computed before
the date arrived would keep its stale null and go on being excluded from every
strict read.

Separately, **an action whose factor no discontinuity supports raises a
`ActionWithoutDiscontinuity` finding.** Rescale the last close before the
ex-date by the factor; the ex-date's own close should land within one ordinary
session of it. Further than the venue's daily band allows did not come from the
market. The factor is stored regardless — refusing it would lose the record of
what the source said.

## Alternatives

**Filter in SQL by joining `corporate_actions`.** Correct, and pays a join on
the hot read path to fetch one nullable date per factor. The copy is derived
data kept honest by a staleness check that already exists for the version and
the reference close.

**Make strict the only behaviour.** Honest, and unusable. Most Vietnamese
action records carry no announcement date, so every chart would show splits as
crashes — and the first response would be to stop asking for adjusted prices,
which is worse than either reading.

**Infer the announcement date from the ex-date minus a typical notice period.**
Manufactures a fact. The whole point of the column is to record what a source
said; a system that fills it in has no way to distinguish a real date from its
own guess, and every strict read afterwards is measuring the guess.

**Treat a missing `announced_on` as a data-quality finding.** It is not a
defect in the data. The source genuinely did not publish one, and raising a
finding on the overwhelming majority of Vietnamese action records would bury
the findings that mean something.

**Raise the discontinuity finding as a fixed percentage threshold.** Would need
a number nobody can justify. The venue's own band is the number the market
already enforces, and reusing it keeps this check and the price-limit check
from disagreeing about what an ordinary day is.

## Reasoning

The policy is explicit because the two readings cannot be reconciled and the
choice belongs to whoever is asking the question. A backtest and a chart want
opposite things from the same missing column, and a system that picked one
silently would make the two indistinguishable in the answer — which is exactly
the failure this codebase keeps refusing elsewhere.

Reporting `adjustmentsWithheld` is what makes the strict reading legible. A
series that quietly under-adjusts looks like bad data; the same series with a
count beside it saying two actions were held back is a correct answer to a
point-in-time question.

The discontinuity check is the inverse of the price-limit check and reuses its
machinery. A breach says the prices moved and nothing explains it; this says
something claims to explain a move that never happened. Neither can be decided
from prices alone, which is why both are findings rather than corrections.

## Trade-offs

**A strict read can under-adjust, sometimes badly.** On a series whose actions
carry no announcement dates, strict returns something close to the raw series.
That is the intended failure direction, and the withheld count says so.

**`announced_on` is duplicated.** Two rows can disagree until a recompute runs.
The staleness check catches it, and the migration backfills the existing rows
so nothing starts out wrong.

**The discontinuity check needs the ex-date's bar.** An action whose ex-date
falls outside the ingested history is not checked. Reported as nothing rather
than as a pass — an unchecked action is not a verified one.

**One more read per computed factor.** Only on recompute, only for factors that
changed, and a recompute already reads the bars around the ex-date.

## Consequences

- `price_adjustments` carries `announced_on`, backfilled from
  `corporate_actions`.
- `BarQuery` carries `AnnouncementPolicy`; `BarSeries` and the bars endpoint
  carry the policy, the instant, and the applied and withheld counts.
- `GET /instruments/{id}/bars` accepts `announcementPolicy=strict|permissive`.
- The console's `GP` accepts `--strict`, and refuses it without `--as-of`.
- `DataQualityIssueKind.ActionWithoutDiscontinuity` exists and is raised by the
  adjustment recompute.
- ADR-018's stated gap is closed. The caveat text in `BarQuery`, `SeriesBar`
  and the bars endpoint is replaced rather than left to rot.
- U5's dataset export must request `Strict`. A canonical dataset built under
  the permissive reading would carry look-ahead into every experiment run
  against it, and the manifest records which policy produced it.
