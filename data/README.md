# Data Directory

This directory is the single agreed location for local datasets used by the
terminal. It exists so that data never leaks into source directories and so
that the Git policy for data is explicit rather than accidental.

**Status:** three interchange contracts under `schemas/`, and small synthetic
fixtures under `fixtures/` that let the import and ingestion pipelines run on a
fresh clone. No vendor dataset is present, and none may be added — see the
rules below.

## Layout

```
data/
├── README.md      tracked   this file
├── schemas/       tracked   schema definitions (JSON Schema, DDL, Avro, ...)
├── fixtures/      tracked   tiny synthetic fixtures used by tests
├── raw/           IGNORED   unmodified provider downloads
├── interim/       IGNORED   partially processed data
├── cache/         IGNORED   provider response cache
├── external/      IGNORED   third-party datasets
└── tmp/           IGNORED   scratch space
```

Only `README.md`, `schemas/`, and `fixtures/` are tracked by Git. See the
`# Local data` block in the repository `.gitignore`.

## Rules

1. **Never commit vendor market data.** Not prices, not fundamentals, not news
   bodies. Redistribution rights are almost never granted by default.
2. **Never commit large files.** Anything above a few hundred kilobytes belongs
   outside Git.
3. **Fixtures must be synthetic or trivially small.** A fixture exists to make a
   test deterministic, not to be a dataset.
4. **Schemas are code.** They belong in `schemas/`, are reviewed, and are
   tracked.
5. **Provider credentials never live here.** They belong in `.env`, which is
   git-ignored.

## Licensing

Market data carries its own licensing independent of this repository's
proprietary license. Before any provider is integrated, its terms must be
evaluated against the checklist in
[`docs/architecture/data-policy.md`](../docs/architecture/data-policy.md).
