# ADR-012: Identifier aliases and the provider import pipeline

**Status:** Accepted · **Date:** 2026-08-26 · **Phase:** 1 (workstreams 3, 4, 5, 6)

## Context

[ADR-009](ADR-009-instrument-identity-and-ticker-lifecycle.md) established that
an instrument's identity is an internal `InstrumentId` and that the ticker is a
mutable attribute. That decision states a promise the system had not yet kept:
*every provider's spelling of a security maps to the same canonical ID.*

Nothing could keep it, because nothing recorded the spellings. The only route
from a provider's record to an instrument was the ticker, and a ticker is
neither unique across venues nor stable across an issuer's life. Two problems
followed directly:

- **Two vendors, two spellings.** `FPT.HM` and `FPT:VN` are the same security.
  Without aliases the master either creates it twice or matches on a ticker and
  gets it wrong the first time an issuer transfers venue.
- **Nothing populated the master.** Instruments existed only where a
  development seed put them, which is not a system of record.

## Decision

**Aliases are a separate table, and never identity.** `InstrumentIdentifier`
holds a scheme, a value, an optional source and the instrument it names.
`InstrumentId` remains the key everything joins on: an ISIN is licensed
reference data its issuing agency can reassign, and a provider symbol belongs
to the provider.

**Three schemes, no more.** ISIN, FIGI and provider symbol — what the roadmap
named. CUSIP and SEDOL are one enum value and one validation rule each, added
when something maps against them.

**Global identifiers are validated by check digit.** An ISIN or FIGI failing
its check digit is refused. A check digit catches a typed or transposed
character and nothing else — it does not prove the identifier exists or belongs
to the security it was filed against — but an identifier with a corrupt
character maps to nothing, silently and permanently, because nothing
downstream revisits it.

**Uniqueness is enforced by two partial unique indexes.** An ISIN or a FIGI
resolves to one instrument across the whole master; a provider symbol resolves
to one instrument *per provider*, because two vendors legitimately use the same
decorated symbol for different securities. Both are database constraints rather
than conventions the pipeline is trusted to follow.

**Symbol normalisation splits, it does not strip.** `FPT`, `FPT.HM`,
`FPT:VN`, `HOSE:FPT` and `FPT-HNX` all yield the ticker `FPT` plus an optional
venue hint, and the provider's exact spelling is stored as an alias so the next
import is a lookup rather than a re-parse. A symbol containing two possible
tickers is refused rather than resolved by picking the first.

**Deduplication is ordered by how much a match can be trusted:** this
provider's own symbol, then a global identifier, then the ticker on its venue.
A ticker is last because it identifies a listing rather than a security.

**A row whose routes disagree is rejected, not resolved.** An ISIN pointing at
one instrument and a ticker at another means either the master has a duplicate
or the provider has a mistake. Both need a human.

**Import never deletes, delists, or overwrites.** A security absent from a
provider's list has not necessarily stopped trading. A vendor's spelling of a
company name is not the registered one. A listing date it invented would
overwrite a sourced one. Enrichment is additive and limited to aliases.

**One transaction per run, and a report rather than an exception.** A
half-applied import leaves aliases pointing at instruments that were never
created; a run that throws on row nine of four thousand imports nothing.

**`GET /instruments` includes delisted rows unless a status is given** — the
opposite of search's default. This is the read historical work uses, and
silently omitting delisted securities from a universe is how survivorship bias
enters a backtest.

**There is no endpoint that triggers an import.** It reads an external source
and writes to the system of record.

## Alternatives

**A ticker-first match.** Rejected: it is the weakest signal available and the
one that breaks precisely where the instrument master is supposed to help —
after a delisting reassigns a ticker, or an exchange transfer changes it.

**Storing only a normalised ticker and discarding the provider's decoration.**
Rejected: the next import from that vendor would have to re-derive the mapping,
and any ambiguity it could not resolve would recur every night rather than
once.

**Repairing rather than rejecting a conflicting row.** Rejected. Every
automatic resolution either merges two securities or splits one, and both are
invisible afterwards.

**Letting import correct names, asset classes and listing dates.** Rejected for
now. It sounds like enrichment and is actually an unaudited write to the system
of record from a source whose authority has not been established. The seeder
takes the same position for the same reason.

**Relaxing global uniqueness to permit cross-listings.** Rejected while the
market is Vietnam-only, where a security lists on one venue at a time. The
consequence is recorded below.

**A shared-identifier relation on `/related`.** Dropped during implementation.
Global uniqueness makes two instruments carrying one ISIN impossible, so the
branch would be one the database forbids from ever being taken.

## Reasoning

The ordering of the deduplication routes is the substance of this ADR. Each
route is a claim of identity with a different strength of evidence, and
consulting them in the wrong order produces a system that works on the happy
path and fails exactly where reference data is hard.

Everything else follows the pattern the earlier ADRs set: prefer a loud,
recorded refusal to a plausible answer. A rejected row with a typed reason can
be investigated; a silently merged security cannot even be noticed.

## Trade-offs

- **A two-letter listing could not be parsed from a decorated symbol.** `VN` is
  listed as a country qualifier so `FPT:VN` resolves at all. No Vietnamese
  listing uses a two-letter ticker, and `VNM` is deliberately *not* in that
  list because it is Vinamilk.
- **Global uniqueness bakes in a Vietnam assumption.** A cross-listed universe
  needs the constraint relaxed to include the exchange, and the
  shared-identifier relation becomes reachable at the same moment.
- **Import holds one transaction over the whole run.** Correct, and it puts an
  upper bound on how large a symbol list can be before the transaction is worth
  splitting. A market-sized list is a few thousand rows; a global one would not
  be.
- **Enrichment being alias-only means correcting a record is manual.** That is
  the intended trade for a system of record, and it will feel like friction the
  first time a name is wrong.
- **A check digit is not proof.** A well-formed ISIN belonging to a different
  security passes. Only a licensed reference source can catch that, and none is
  integrated.

## Consequences

- Phase 2's market data ingestion can address a provider in that provider's own
  symbology and store the result against the canonical identifier, because the
  mapping now exists.
- A second market data vendor is additive: implement the provider, import its
  symbol list, and its spellings resolve to instruments that already exist.
- Adding a scheme is one enum value, one validation rule, and a decision about
  whether it is globally or provider-scoped.
- The instrument master is now populated from a source that can be cited rather
  than from a development seed, which is what Phase 3's data quality work
  assumes when it starts scoring completeness.
