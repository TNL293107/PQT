# Provider capability and selection

**Status: DESIGNED.** Nothing here is implemented. This is the implementation
specification for the provider-plurality scope note on
[U3](../roadmap/pqt-roadmap-v2.md#u3--real-vietnamese-data-provider-integration--gate-a--mandatory),
written before any code so that the contract and the acceptance criteria are
settled first.

The ideas behind it — capability declared rather than discovered, explicit
selection, and the rejection of automatic fallback — came from the OpenBB
review recorded in [`openbb-evaluation.md`](openbb-evaluation.md) and
[ADR-019](decisions/ADR-019-openbb-boundary.md). Nothing in this document
depends on OpenBB, and none of it is an OpenBB integration.

---

## Why now

U3 is the first moment PQT holds **two** market data providers. Today it holds
one — `FileMarketDataProvider`, code `FILE` — and one provider is the case
every seam in the ingestion pipeline was tested against.

Three things break at exactly two, and each of them is cheaper to fix before
real data is flowing than after.

### Defect 1 — the default provider stops resolving

`MarketDataProviderRegistry.TryResolveDefault` returns a provider only when
exactly one is registered, and the comment explains why: with several, picking
one would attribute bars to whichever won registration order.

That reasoning is right. The consequence is that every instruction which does
not name a source begins failing the moment a second provider appears:

```
MarketDataIngestionService.ResolveProvider  →  null
  →  "No market data source is registered, or several are and none was named."
  →  IngestionRun skipped
```

The run is recorded rather than lost, which is correct behaviour. But the
scheduled host (`MarketDataIngestionHostedService`) names no source, so a
second provider silently converts the entire nightly pass into skipped runs.

### Defect 2 — capability is one property

A provider declares `SupportedIntervals` and nothing else. The XML documentation
already states the principle:

> Declared rather than discovered by failure. A provider that only has
> end-of-day data should cause an intraday request to be skipped with a reason,
> not to fail after three retries against an endpoint that was never going to
> answer.

That principle is right and it is applied to exactly one dimension. A provider
that covers HOSE but not UPCOM, or holds no history before 2015, or reports no
turnover, has no way to say so — and every one of those is a retry loop against
an endpoint that was never going to answer.

### Defect 3 — silent cross-source takeover · **the one that matters**

This is a live defect in committed code, not a gap in a planned feature.

`OhlcvBar.Revise` includes the source in its unchanged comparison:

```csharp
var unchanged =
    Open == open && High == high && Low == low && Close == close
    && Volume == volume && Turnover == turnover
    && Source == source;      // ← here
```

So when provider `B` fetches a period provider `A` already stored, **with
byte-identical prices and volume**, `unchanged` is false. The bar is revised:
`Source` is rewritten to `B`, `RevisedAtUtc` is set, `Revision` is incremented,
and `MergeAsync` appends a `BarRevision` row and supersedes the open one.

Nothing changed about the fact. Only who claimed it did.

Three consequences follow, in ascending order of seriousness:

| | Consequence |
| --- | --- |
| 1 | The series quietly changes owner. `quant.bars.source` now says `B` for periods `A` produced |
| 2 | `Revision` counts re-attributions as restatements. The roadmap defines a revision as *"the ordinal identity of one statement of a fact"* — and the fact did not get a new statement |
| 3 | The observation history U1 has just landed (`beacd70`) records a revision event where no value moved, so `bar_revisions` no longer answers *what did the number say at T* without also answering *who was asked* |

The third is the expensive one. U1 exists to make point-in-time reads truthful,
and source churn injects revision rows that carry no information about the
value. A backtest replaying observation history would see restatement activity
that never happened.

**And the checkpoints make it likely rather than theoretical.**
`IngestionCheckpoint` is keyed on `(instrument, interval, source)`, so provider
`B` starts with no checkpoint and backfills from `policy.InitialBackfill` —
365 days by default — straight across everything `A` already holds. Registering
a second provider and running the scheduler once is sufficient to trigger it.

---

## What U3 adds

Three deliverables, in dependency order. Each lands independently and none
requires the next.

```
Provider capability contract          declares what a provider can serve
        ↓
Explicit selection                    resolves one provider, or says why not
        ↓
Source-conflict detection             a mixed series becomes a finding
        ↓
Operator CLI                          the surface that drives all of it
```

---

## 1. Provider capability contract

### The type

Application layer, beside `IMarketDataProvider`. A record, immutable, supplied
by the provider rather than configured about it — a provider is the only thing
that knows what it can serve.

```csharp
public sealed record ProviderCapability
{
    public required SourceCode Code { get; init; }
    public required string DisplayName { get; init; }

    // Markets. Empty means "any venue" — correct for a file source, and
    // never correct for a vendor.
    public IReadOnlySet<ExchangeCode> Exchanges { get; init; } = new HashSet<ExchangeCode>();

    // Assets.
    public IReadOnlySet<AssetType> AssetTypes { get; init; } = new HashSet<AssetType>();

    // Intervals. Replaces IMarketDataProvider.SupportedIntervals as the
    // source of truth; the property stays and delegates here.
    public required IReadOnlySet<BarInterval> Intervals { get; init; }

    // Coverage floor. Null means unknown, which is not the same as unbounded
    // and must never render as it.
    public DateOnly? EarliestAvailable { get; init; }

    public required ProviderReportedFields ReportedFields { get; init; }
    public required ProviderLimitations Limitations { get; init; }
}
```

`ReportedFields` and `Limitations` are the two halves of *metrics* and
*limitations* — separated because one is about what arrives and the other is
about how it may be asked for:

```csharp
public sealed record ProviderReportedFields
{
    public bool Turnover { get; init; }
    public bool AnnouncementDates { get; init; }   // gates U4 strict mode
    public bool Restatements { get; init; }        // gates reproducible backtests
}

public sealed record ProviderLimitations
{
    public int? MaxPeriodsPerCall { get; init; }
    public TimeSpan? MinimumCallSpacing { get; init; }
    public bool AdjustsPricesAtSource { get; init; }   // PQT adjusts on read; a
                                                       // pre-adjusted feed is a
                                                       // different dataset
}
```

### Three properties this design commits to

**Absent is not unlimited.** `EarliestAvailable = null` means *not stated*, and
every surface that renders it must say so. This is the same rule U2 applies to
universe membership: an empty coverage claim and a complete one must never be
indistinguishable.

**Capability is declared, never measured.** Nothing probes a provider to
discover what it can do. A wrong declaration is a provider bug, reported as
such, and it fails loudly against a request the provider claimed to serve.

**`AdjustsPricesAtSource` is not a detail.** PQT's whole Phase 4 shape is raw
prices plus adjustment on read. A feed that adjusts at source produces a series
that is *already* a derived view, and mixing it with a raw series is
meaningless. It is declared here so that mixing can be refused rather than
discovered in a backtest.

### Effect on `IMarketDataProvider`

```csharp
public interface IMarketDataProvider
{
    SourceCode Code { get; }
    ProviderCapability Capability { get; }                 // new
    IReadOnlySet<BarInterval> SupportedIntervals { get; }  // delegates to Capability.Intervals
    Task<MarketDataFetchResult> FetchBarsAsync(...);
}
```

`SupportedIntervals` stays. Removing it would touch the ingestion service, the
tests and `FileMarketDataProvider` for no behavioural gain, and one derived
property is cheaper than a rename that ripples.

---

## 2. Explicit selection

### The type

```csharp
public sealed record ProviderSelection
{
    public IMarketDataProvider? Provider { get; }
    public ProviderSelectionOutcome Outcome { get; }
    public string? Reason { get; }       // caller-safe, names what was missing
}

public enum ProviderSelectionOutcome
{
    Selected = 1,
    Unknown = 2,       // named, not registered
    Ambiguous = 3,     // several could serve it, none named
    Incapable = 4,     // named and registered, cannot serve this request
    None = 5,          // nothing registered can serve it
}
```

Selection lives in `IMarketDataProviderRegistry` as
`Select(MarketDataRequest-shaped criteria)`, replacing `TryResolveDefault` at
the call site while leaving `TryResolve(code)` alone.

### The rule that changes

Today the default resolves when **exactly one provider is registered**. It
should resolve when **exactly one registered provider can serve the request**.

That is a strictly better rule and a small change. A deployment holding a
Vietnamese daily provider and an intraday-only provider has no ambiguity for a
daily HOSE request — only one candidate can serve it — and today it has an
error.

Ambiguity remains an error, and deliberately so. **There is no tie-break, no
priority order and no fallback.** Two providers that could both serve a request
are two different answers to the same question, and choosing between them by
configuration order would attribute the series to whichever was registered
first.

### Why fallback is refused

Recorded here because it will be proposed again.

Falling through to a second provider when the first is unavailable assembles
one series from two symbologies, two adjustment conventions, two restatement
policies and two definitions of a session. The bars would carry different
`source` values, which is honest — and every consumer that reads a series
rather than a row would inherit the mixture without a way to notice.

PQT's answer is that the mixture is made **visible**, not made **easy**. A
provider that cannot answer produces a recorded failed run, and the operator
decides.

### Selection reasons must be specific

`Incapable` must say which dimension failed and what was asked for:

```
'VNX' does not serve 5m bars.
'VNX' does not cover UPCOM.
'VNX' holds nothing before 2015-01-02; the request starts 2010-03-01.
```

Not `"the provider cannot serve this request"`. The reason lands in
`IngestionRun.Skip`, which is the record that explains a gap in a series, and a
vague reason there is a gap nobody can close.

---

## 3. Source-conflict detection

### The fix, first

`OhlcvBar.Revise` must stop treating re-attribution as restatement. Remove
`Source` from the unchanged comparison, and leave `Source` untouched when the
values are identical:

```csharp
var unchanged =
    Open == open && High == high && Low == low && Close == close
    && Volume == volume && Turnover == turnover;

if (unchanged)
{
    return false;      // no revision, no history row, source unchanged
}
```

A value-identical fetch from another provider is then exactly what it is:
corroboration, and a no-op.

**Coordination point.** This touches the observation history landed in
`beacd70`. The `BarRevision` invariant — *revision 0's `observed_from_utc`
equals the bar's `ingested_at_utc`* — is unaffected, because no revision row is
written at all. The existing U1 tests must be re-run and
`BarRevisionPersistenceTests` extended with a same-values-different-source
case.

Note while doing so that
[`data-architecture.md`](data-architecture.md#required-tests) lists required
test 5 as *"a property test over randomised revision sequences"*. What exists
is `Every_instant_across_several_corrections_has_exactly_one_answer`, which is
deterministic over several corrections. The property it asserts is the right
one; the doc describes a test shape that was not built, and that discrepancy
should be settled in the document rather than papered over here.

### When values actually differ

Two providers disagreeing about a close is a real event and must not be silently
resolved by whichever ran last. The bar is still revised — the pipeline's rule
that raw data is retained and findings are recorded rather than corrected
applies here as everywhere — and a finding is raised.

```csharp
public enum DataQualityIssueKind
{
    PriceLimitBreach = 1,
    MissingSession = 2,
    UnexpectedSession = 3,
    SourceConflict = 4,      // new
}
```

Raised when a run revises a bar whose stored `Source` differs from the running
provider's code **and** at least one value differs. Detail carries both sources
and both disputed values.

The existing uniqueness — one issue per instrument, resolution, session and
kind — applies unchanged, so a nightly re-read raises nothing new and a
dismissal is not undone.

### Where it is detected

In `MarketDataIngestionService.MergeAsync`, which already holds both the stored
bar and the incoming one, already has the running provider's `SourceCode`, and
already stages findings inside the caller's unit of work through
`IBarQualityInspector`. No new mechanism, and the finding commits in the same
transaction as the bar it concerns.

### What this does *not* fix

`SourceCode = FILE` still collapses every file-delivered origin into one code.
Source-conflict detection makes a *mixed* series visible; it does nothing about
a series whose true origin was never recorded. That is the provenance-model
work, it is a separate piece, and it must not be smuggled into U3.

---

## 4. Operator CLI

### Why a CLI and not an endpoint

The architecture overview records that there is deliberately **no HTTP write
surface at all**, and that a trigger endpoint waits for authentication that does
not exist yet.

A CLI needs none. It runs where the operator already holds the connection
string, so it is the surface that can exist today, and it is the surface U3's
already-scoped bulk-load backfill path needs.

### Shape

A `PersonalQuant.Cli` console project referencing `Application` and
`Infrastructure` and composing them the same way `Api` does.

```
pqt provider list
pqt provider show <CODE>
pqt provider check <CODE> --instrument FPT --interval 1d --from 2015-01-01

pqt ingest run       --instrument FPT --interval 1d --source VNX [--from --to]
pqt ingest backfill  --universe VN30  --interval 1d --source VNX --from 2015-01-01

pqt quality list --instrument FPT --status open
```

### The rule the CLI is bound by

> **The CLI holds no business logic.** Every command parses arguments, calls one
> existing application service, and renders the result.

`ingest run` calls `IMarketDataIngestionService.IngestAsync`. `provider check`
calls the selection logic from §2 and prints the outcome. `backfill` is a loop
over `IngestAsync` — the checkpoint already makes a long range resume across
runs, so backfill is repetition, not a second pipeline.

A command that computes something no other caller can reach is the boundary
having been breached. This is the same one-core-many-interfaces rule the
roadmap states for REST, the Python facade and MCP.

---

## Validation rules

| # | Rule | On violation |
| --- | --- | --- |
| V1 | A request's interval must be in the provider's declared intervals | `Incapable` · run skipped with the interval named |
| V2 | The instrument's exchange must be in the provider's declared exchanges, when it declares any | `Incapable` · run skipped with the venue named |
| V3 | The instrument's asset type must be in the provider's declared asset types, when it declares any | `Incapable` · run skipped with the type named |
| V4 | A request may not start before `EarliestAvailable`, when it is stated | Range clamped forward, and the clamp recorded on the run |
| V5 | Exactly one registered provider must be able to serve an unnamed request | `Ambiguous` · run skipped listing the candidates |
| V6 | A named provider must be registered | `Unknown` · run skipped naming the code |
| V7 | A revision that changes only the source is not a revision | No revision, no history row, source unchanged |
| V8 | A revision that changes values across a source boundary raises `SourceConflict` | Bar revised, finding raised, both sources and values in the detail |
| V9 | A provider declaring `AdjustsPricesAtSource` may not write into a series held by one that does not | Refused before fetch, with both providers named |
| V10 | `MaxPeriodsPerCall`, when stated and lower than `MarketDataRequest.MaxPeriods`, bounds the range | Range truncated; the checkpoint resumes, as it already does |

V9 is the strictest rule here and is deliberate. A raw series and a
source-adjusted series are different datasets that happen to share a shape, and
merging them produces numbers that are wrong in a way no quality rule can see.

---

## Acceptance criteria

Gate-A-relevant, and each one falsifiable.

**Capability**

- [ ] Every registered provider exposes a `ProviderCapability`, and `FileMarketDataProvider` declares one that describes what a directory of CSVs actually offers — every interval, no exchange restriction, no stated coverage floor.
- [ ] A request outside a declared capability is skipped **before** any call is made. Asserted by a provider whose `FetchBarsAsync` throws if reached.
- [ ] An unstated `EarliestAvailable` renders as *unknown* and never as *unbounded*, in every surface that renders it.

**Selection**

- [ ] With two providers registered and only one able to serve the request, an unnamed instruction resolves to that one.
- [ ] With two providers able to serve it, an unnamed instruction is `Ambiguous` and the run's reason lists both candidates.
- [ ] A named, unregistered code is `Unknown` and the reason names the code.
- [ ] Every `Incapable` reason names the dimension that failed and the value that was asked for.
- [ ] **No configuration exists that causes one provider to be tried after another fails.** Asserted by a test that registers a failing provider and a working one and proves the run fails.

**Source conflict**

- [ ] Provider `B` re-fetching a period stored by `A` with identical values produces **no** revision, **no** `bar_revisions` row, and leaves `source = A`.
- [ ] Provider `B` re-fetching with different values revises the bar and raises exactly one `SourceConflict` finding carrying both source codes and both values.
- [ ] Re-running the same conflicting fetch raises no second finding, and does not reopen a dismissed one.
- [ ] The full U1 point-in-time suite passes unmodified, and `BarRevisionPersistenceTests` is extended with a same-values-different-source case asserting no observation window is written.
- [ ] A `SourceConflict` finding is committed in the same transaction as the bar that triggered it.

**CLI**

- [x] `pqt provider list` and `pqt provider show` render declared capability, including the unknowns as unknown.
- [x] `pqt ingest run` produces an `IngestionRun` indistinguishable from one the scheduled host produces.
- [x] `pqt ingest backfill` over a range longer than `MaxPeriods` completes across several runs and leaves the checkpoint where the last stored bar is.
- [x] No CLI command contains a branch that is not reachable through the application layer by another caller.

Two criteria were added while building it, because the first smoke test found
what they describe.

- [x] A command line the operator got wrong is answered before anything reaches the deployment. Asserted by a harness whose every service throws when constructed.
- [x] `provider list` and `provider show` run on a host with no database configured. They read declarations that live in the composition root, and that is the state in which the question is most worth asking.

**Regression**

- [ ] With one provider registered and no source named, behaviour is byte-identical to today. This is what makes the change safe to land — the existing suite passes unmodified.

---

## Task breakdown

Ordered by dependency. Each task lands on its own and leaves the build green.

| # | Task | Depends on | Touches |
| --- | --- | --- | --- |
| **T1** | `ProviderCapability`, `ProviderReportedFields`, `ProviderLimitations` records, with validation | — | Application/MarketData |
| **T2** | Add `Capability` to `IMarketDataProvider`; `SupportedIntervals` delegates; `FileMarketDataProvider` declares one | T1 | Application, Infrastructure |
| **T3** | `ProviderSelection` and `ProviderSelectionOutcome`; registry `Select` | T2 | Application/MarketData |
| **T4** | Ingestion service uses `Select`; V1–V6 and V10 produce specific skip reasons | T3 | MarketDataIngestionService |
| **T5** | **Fix `OhlcvBar.Revise`** — source excluded from the unchanged check | — | Domain/MarketData, U1 tests |
| **T6** | `DataQualityIssueKind.SourceConflict`; raise it from `MergeAsync`; migration for the enum value | T5 | Domain, Application, EF migration |
| **T7** | V9 — refuse mixing source-adjusted and raw series | T2, T6 | MarketDataIngestionService |
| **T8** | `PersonalQuant.Cli` project, composition, `provider list` / `show` / `check` | T3 | new project, CI |
| **T9** | `ingest run` / `ingest backfill` / `quality list` | T8, T4 | Cli |
| **T10** | ADR recording the selection model and the rejection of fallback | T4, T6 | docs/architecture/decisions |

**T5 is independently valuable and has no dependencies.** It is a live defect in
committed code and can land first, before any capability work, which is the
recommended order.

### Where the tasks stand

| # | Status |
| --- | --- |
| T1 · T2 · T3 · T4 | Landed — capability declared, selection explicit, skip reasons specific |
| T5 · T6 | Landed — source excluded from the unchanged check; `SourceConflict` raised from `MergeAsync` |
| **T7** | **Landed** — V9 enforced at ingest, refused before fetch, both sources named |
| **T8 · T9** | **Landed** — `PersonalQuant.Cli`, six commands; see [`../development/operator-cli.md`](../development/operator-cli.md) |
| T10 | Landed — ADR-015 records the selection model and the rejection of fallback |

`ProviderReportedFields` also carries `VolumeBasis` now. Vietnamese venues run
a matched book and a negotiated one, a feed may publish either or their sum, and
the number is identical whichever it is — so a liquidity screen built on it
means something different depending on a fact nothing was recording. It is
declared, rendered by `provider show`, and `Unspecified` is not a synonym for
*everything*. Mixing bases is not refused, because only one registered source
states a basis and a rule would guard a case that cannot occur; when a second
does, the refusal belongs beside V9. See
[ADR-015](decisions/ADR-015-vietnam-market-data-provider.md).

V9 was the last rule still enforced on the read path alone. The adjusted read
already declined to rescale a series whose source had rescaled it, which keeps
one answer correct and lets the wrong data into the table underneath it. It is
now refused at ingest, before the fetch, and **symmetrically** — the table above
states the rule one way round, and refusing only that direction would leave the
mixture reachable by running the two sources in the other order.

Two sources sharing an adjustment convention are still allowed to meet. That is
a restatement and a `SourceConflict` finding, and folding it into this refusal
would remove the machinery that makes a real disagreement visible.

---

## Scope fence

Named so that the work does not grow.

| Not in U3 | Where it belongs |
| --- | --- |
| Provider health, availability probing, circuit breaking | Nowhere yet. No workload justifies it |
| Provider priority or automatic fallback | **Rejected**, not deferred |
| Credential management, key rotation, per-provider secrets | Phase 19 — Production Hardening |
| A registry of what data **PQT holds** | Phase 5 — dataset & coverage registry. U3 answers what a provider *can serve*; Phase 5 answers what PQT *has*. The two connect; they do not merge |
| Fixing `SourceCode = FILE` collapsing true origin | Provenance-model work, separate and after Phase 5 |
| Fundamentals, news, macro or any non-bar dataset | Phase 6 and Phase 11. `ProviderCapability` is shaped so a dataset dimension can be added without a rewrite, and that dimension is not added now |
| Any OpenBB adapter | `DEFERRED` — [ADR-019](decisions/ADR-019-openbb-boundary.md) |
