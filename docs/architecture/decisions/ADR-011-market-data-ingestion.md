# ADR-011: Market data ingestion, provenance and resume state

**Status:** Accepted · **Date:** 2026-08-26 · **Phase:** 2

## Context

Phase 2 is the first backbone phase. Everything after it — normalisation
scoring, corporate-action adjustment, backtesting, risk — computes on whatever
this phase stores, and none of those can detect a fault they inherited. The
dependency chain in the roadmap says it plainly: data wrong → backtest wrong →
risk wrong → trading wrong.

Four questions had to be answered before any bar could be written.

- **Where does a bar come from, and how is it addressed?** No vendor may be
  hard-coded, and the deployment must be runnable on a fresh clone by someone
  holding no licence — Vietnamese market data is licensed, and a repository
  that can only be demonstrated with a paid key cannot be demonstrated.
- **What is a bar's identity?** A time series stored with a generated key can
  hold the same period twice, and nothing at the database level objects.
- **What happens when a run fails?** A gap with no record beside it is
  indistinguishable from a public holiday.
- **What happens when a provider was wrong?** Both about the data it sent and
  about the way this system parsed it.

## Decision

**A bar's identity is `(instrument, interval, opening instant)`, and that is
the primary key.** There is no surrogate. Deduplication is therefore a schema
property rather than a rule in whatever code is writing.

**The opening edge, never the closing one.** A daily bar opens at midnight UTC
on the session's trading date, which works because every venue in scope trades
at UTC+7 and a session lies wholly inside one UTC day. Recorded as a convention
to revisit, not a law.

**Prices are `numeric(18,6)` and a `Price` value object.** Strictly positive,
scale bounded at construction so the value in memory and the value on disk are
the same number. Zero is a provider's "no data", not a trade.

**Structural invariants live on the aggregate.** `high >= max(open, close)`,
`low <= min(open, close)`, `high >= low`, `volume >= 0`, and no turnover
without volume. Nothing is repaired — a row that fails is rejected with a
reason, because clamping a high up to a close turns a visible provider fault
into a plausible bar.

**Providers fetch; they do not decide.** `IMarketDataProvider` returns the
payload verbatim and the rows parsed from it. Validation, deduplication,
persistence and checkpointing are the pipeline's, identically for every source.
Retry, timeout and rate limiting are applied *around* a provider rather than
inside it.

**The raw payload is retained beside the canonical bars.** Re-normalising from
raw must always be possible: every normaliser has bugs found later, and for
historical ranges re-fetching is often no longer possible and never free.

**Every attempt is audited, including the ones that did nothing.**
`IngestionRun` records succeeded, failed and skipped, with the counts kept
separate — fetched, accepted, rejected, stored, revised. The differences
between them are the diagnosis.

**The checkpoint advances to the newest bar actually stored,** never to the end
of the requested range, and never backwards.

**Everything a run produces commits in one transaction.** A checkpoint that
survives while its bars do not is the one failure that leaves a permanent,
silent hole.

**A restatement is counted, not applied silently.** `OhlcvBar.Revise` returns
whether anything changed, so re-fetching an unchanged range — the normal case —
is neither a store nor a revision.

**No HTTP endpoint triggers ingestion.** The read endpoints
(`/instruments/{id}/bars`, `/instruments/{id}/ingestion`) exist; the trigger
waits for the authentication in Phase 18.

## Alternatives

**A surrogate key on bars, with a unique index for deduplication.** Rejected:
it is the same constraint written twice, and the extra column exists only to be
ignored.

**`double` for prices.** Rejected outright. Binary floating point cannot
represent a tenth exactly; summed across a backtest the error compounds into
returns the market never produced.

**Providers returning finished, validated bars.** Rejected: a rule implemented
once per vendor is a rule that will eventually differ between them, and a row
that fails validation has to survive long enough to be reported as rejected.

**Storing only the parsed rows, not the payload.** Rejected: it makes every
normalisation bug permanent for any range that cannot be re-fetched.

**Recording only successful runs.** Rejected: the audit table exists to explain
gaps, which it cannot do if failures leave nothing behind.

**Overwriting a restated bar in place with no marker.** Rejected: a series that
has been restated is a different thing from one that has not, and a backtest
whose results move needs to be able to say why.

**A vendor HTTP provider as the reference implementation.** Rejected for now:
it would make the pipeline undemonstrable without a licence, and an interface
with one implementation shaped around one vendor's API is not an abstraction. A
CSV file source is a real provider under the same contract.

**Polly or `System.Threading.RateLimiting` for retry and spacing.** Rejected at
this size: the policy is a few dozen lines, every wait goes through an injected
scheduler so the ladder is asserted in milliseconds rather than slept through,
and the dependency would buy generality nothing needs yet.

## Reasoning

Each decision above is chosen against a specific way of being silently wrong,
not against a style preference. The pattern is consistent: prefer the failure
that is loud and recorded over the one that produces a plausible number.

The half-open range (`[from, to)`) with both edges aligned to a period boundary
is what makes two adjacent requests tile the timeline exactly — no period in
both, none in neither. A closed range duplicates a bar at every seam; an
unaligned edge makes deduplication depend on which run went first.

The period in progress is never requested. A daily bar fetched at midday is a
real number that will be a different real number by the close, and storing it
produces a series whose most recent bar is sometimes provisional with nothing
recording which.

## Trade-offs

- **Storage.** Raw payloads are large relative to the bars derived from them.
  Accepted; they are read only when something must be derived again, and they
  are the only insurance against a parsing bug.
- **No jitter in the backoff.** Deterministic and testable, and correct for a
  single process ingesting one instrument at a time. It becomes wrong the day
  ingestion runs in parallel across a universe.
- **A range longer than one request may carry is truncated, not refused.** A
  large backfill completes over several runs. Simpler than chunking inside the
  pipeline, at the cost of a caller needing more runs to catch up.
- **Rate limiting is per-process.** Two processes against one provider would
  exceed the intended spacing. Acceptable while ingestion is a single host.
- **Tick data is out of scope.** A tick has no open, high, low or close, and
  modelling it as a zero-length interval would put a row shaped like a bar into
  a table that means something else.

## Consequences

- Phase 3 builds its quality scoring on the rejection reasons and the audit
  counts this phase already records, rather than inventing a parallel
  mechanism.
- Phase 4 adds adjusted series as separate data. Raw bars are never
  overwritten by an adjustment; `Revise` is for a source restating a period,
  which is a different thing.
- Any new source implements `IMarketDataProvider` and is registered. It gets
  the validation, deduplication, retry, spacing, audit and checkpointing for
  free, and cannot opt out of them.
- A gap in a series can always be explained by reading
  `/instruments/{id}/ingestion`. If it cannot, that is a defect in this phase.
