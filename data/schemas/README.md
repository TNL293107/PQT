# Schemas

Tracked schema definitions for data that crosses a system boundary — provider
payloads, file interchange formats, and contracts shared between the .NET
backend, the Python quant layer, and the C++ engine.

**Status:** one contract. [`market-data-csv.md`](market-data-csv.md) describes
the CSV export the file-backed market data source reads — a real provider from
the pipeline's point of view, and the one the ingestion path is proved
against without a licence. Vendor payload contracts land here as they are
integrated.

The instrument model itself now exists, but it lives in EF Core migrations —
see the note on ownership below.

`Instrument` and `Exchange` landed with Phase 1 workstream 1 as
`quant.instruments` and `quant.exchanges`; `Sector` and `Industry` followed as
`quant.sectors` and `quant.industries`. `Identifier` remains open. Coverage is
HOSE, HNX and UPCOM.

Phase 2 added `quant.bars` for the canonical series, plus
`quant.market_data_raw_batches`, `quant.ingestion_runs` and
`quant.ingestion_checkpoints` for the provenance and resume state behind it.

Corporate action schemas follow in Phase 4 and must model rights issues and
bonus shares as first-class action types, not as dividend variants.

Conventions when files land here:

- One schema per file, named after the concept (`instrument.schema.json`).
- Prefer JSON Schema for provider payloads and interchange formats.
- Database structure is owned by EF Core migrations under
  `backend/src/PersonalQuant.Infrastructure/Persistence/Migrations`, not by this
  directory. Anything here is documentation of an external contract, not the
  source of truth for the database.
