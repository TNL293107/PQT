# ADR-008: Vietnam market first

**Status:** Accepted · **Date:** 2026-08-14 · **Phase:** 0 (decided before Phase 1)

## Context

Phase 1 defines instrument identity, and that decision propagates into every
later phase: market data keys, corporate action handling, the trading rules the
backtester simulates, and which data providers are even viable.

Identity cannot be designed market-agnostically without either building an
abstraction for markets that do not exist yet, or making assumptions that turn
out to be US-specific. The market has to be chosen first.

The operator is based in Vietnam and researches Vietnamese equities. Provider
documentation, exchange rules, filings and financial statements for HOSE, HNX
and UPCOM are directly accessible; the same information for US markets is
available too, but the operator has no working knowledge of it to sanity-check
data against.

## Decision

Target the **Vietnam market first**: HOSE, HNX, UPCOM, VN30 and Vietnamese
indices.

Design for extension to other markets, but do not build the abstraction until a
second market is actually needed.

## Alternatives

**US markets first.** Better documented, more providers, more open-source prior
art, and a more recognisable portfolio piece internationally.

**Market-agnostic from the start.** Model both, abstract the differences.

**Crypto first.** Free APIs, 24/7 data, no corporate actions at all.

## Reasoning

The deciding factor is the ability to tell correct data from incorrect data.
Phases 2, 3 and 4 are the backbone of this system, and all three depend on the
developer recognising when a number is wrong. A ±14% overnight move is an
obvious data error on HOSE, where the daily limit is ±7%, and entirely
unremarkable on NASDAQ. Building a data quality engine for a market whose
normal behaviour you cannot recognise means building it blind.

Vietnam is also the harder case for corporate actions, which makes it the
better one to design against. Rights issues and bonus share distributions are
routine here and comparatively rare in US markets. A corporate action engine
built for Vietnam handles the US case; the reverse is not true, and discovering
that after the adjustment model is fixed would be expensive.

Market-agnostic from the start was rejected as speculative generality of the
worst kind: an abstraction over two markets when only one has a concrete
requirement. The layering already in place (ADR-001) means adding a second
market later is a bounded piece of work, and by then the first market will have
shown which differences actually matter.

Crypto was rejected despite its convenience. No corporate actions, no
settlement cycle and no filings means skipping precisely the phases that make
this a financial systems project rather than a price-charting exercise.

The portfolio argument favours US markets, and it is real. It is outweighed by
correctness: a Vietnamese system that is right is worth more than a US system
whose errors go unnoticed. Nothing prevents adding US coverage in a later phase
once the model is proven.

## Trade-offs

- Fewer providers, less mature APIs, and thinner documentation than US markets.
- Less open-source prior art to borrow from.
- Less immediately recognisable to an international reviewer, requiring the
  portfolio material (Phase 19) to explain the market context.
- Some Vietnam-specific logic — price limits, T+ settlement, lot sizes, ATO/ATC
  auctions — will need generalising if a second market is added.
- Intraday and tick data availability is limited and provider-dependent.

## Consequences

- **Instrument identity is internal.** FIGI, ISIN and CUSIP are not
  meaningfully available for Vietnamese equities, so the canonical ID is
  issued by this system and provider symbols are stored as aliases. That was
  already the correct design (ADR-002); here it becomes the only option.
- **Tickers are reused and reassigned.** Vietnamese tickers change on exchange
  transfer and can be reissued after delisting, so the ticker can never be a
  primary key and instrument lifecycle state is required from Phase 1.
- **Exchange transfer is a normal event**, not an edge case. UPCOM → HNX → HOSE
  is a common progression and the instrument model must preserve identity
  across it.
- **Data quality thresholds are exchange-specific.** ±7% HOSE, ±10% HNX, ±15%
  UPCOM are the bounds Phase 3 validates against, not one global number.
- **The backtester must model Vietnamese microstructure** from Phase 9: T+
  settlement, lot size, price limits, ATO/ATC auction sessions, sell-side tax.
  Omitting them produces returns the market could not have delivered.
- Currency is VND throughout. Multi-currency support is deferred until a market
  requiring it is added.
- Revisit if the project expands to a second market. That warrants a
  superseding ADR covering the market abstraction, not an amendment here.
