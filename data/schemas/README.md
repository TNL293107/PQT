# Schemas

Tracked schema definitions for data that crosses a system boundary — provider
payloads, file interchange formats, and contracts shared between the .NET
backend, the Python quant layer, and the C++ engine.

**Status:** four contracts, all read by file-backed sources that are real
providers from the pipelines' point of view — the ones the ingestion and import
paths are proved against without a licence.

| Contract | Read by |
| -------- | ------- |
| [`instrument-csv.md`](instrument-csv.md) | the instrument import pipeline |
| [`market-data-csv.md`](market-data-csv.md) | the market data ingestion pipeline |
| [`trading-calendar-csv.md`](trading-calendar-csv.md) | completeness scoring, via the calendar import |
| [`corporate-action-csv.md`](corporate-action-csv.md) | the corporate action import, and the adjusted read behind it |

Vendor payload contracts land here as they are integrated.

The instrument model itself now exists, but it lives in EF Core migrations —
see the note on ownership below.

`Instrument` and `Exchange` landed with Phase 1 workstream 1 as
`quant.instruments` and `quant.exchanges`; `Sector` and `Industry` followed as
`quant.sectors` and `quant.industries`; `Identifier` completed the set as
`quant.instrument_identifiers`. Coverage is HOSE, HNX and UPCOM.

Phase 2 added `quant.bars` for the canonical series, plus
`quant.market_data_raw_batches`, `quant.ingestion_runs` and
`quant.ingestion_checkpoints` for the provenance and resume state behind it.

Phase 3 added `quant.trading_holidays` and `quant.data_quality_issues`, gave
`quant.exchanges` a daily price limit, and gave every bar the lineage columns
that record which rules produced it and which have checked it.

Phase 4 added `quant.corporate_actions` and `quant.price_adjustments`. Rights
issues and bonus shares are first-class action types there rather than dividend
variants, because in this market they are routine and their maths is not a
dividend's — a rights issue's factor depends on the subscription price, and a
bonus issue changes the share count without any cash moving.

`quant.bars` is untouched by all of it. Adjustment is a factor applied on read,
never a rewrite — see
[ADR-014](../../docs/architecture/decisions/ADR-014-corporate-actions-and-adjusted-prices.md).

Conventions when files land here:

- One schema per file, named after the concept (`instrument.schema.json`).
- Prefer JSON Schema for provider payloads and interchange formats.
- Database structure is owned by EF Core migrations under
  `backend/src/PersonalQuant.Infrastructure/Persistence/Migrations`, not by this
  directory. Anything here is documentation of an external contract, not the
  source of truth for the database.
