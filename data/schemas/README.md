# Schemas

Tracked schema definitions for data that crosses a system boundary — provider
payloads, file interchange formats, and contracts shared between the .NET
backend, the Python quant layer, and the C++ engine.

**Phase 0 status:** empty by design. No financial schema is defined yet.

The instrument-identity schema (`Instrument`, `Exchange`, `AssetType`,
`Sector`, `Industry`, `Currency`, `Identifier`) arrives in Phase 1 — Instrument
Master, together with the corresponding EF Core model and migration. Coverage
is HOSE, HNX and UPCOM.

Corporate action schemas follow in Phase 4 and must model rights issues and
bonus shares as first-class action types, not as dividend variants.

Conventions when files land here:

- One schema per file, named after the concept (`instrument.schema.json`).
- Prefer JSON Schema for provider payloads and interchange formats.
- Database structure is owned by EF Core migrations under
  `backend/src/PersonalQuant.Infrastructure/Persistence/Migrations`, not by this
  directory. Anything here is documentation of an external contract, not the
  source of truth for the database.
