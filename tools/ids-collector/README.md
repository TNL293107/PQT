# IDS collector

Collects financial-statement filings from the SSC disclosure portal,
[congbothongtin.ssc.gov.vn](https://congbothongtin.ssc.gov.vn), for the
backend's `IDS` fundamentals provider. Why IDS, and what it may be used for,
is [ADR-029](../../docs/architecture/decisions/ADR-029-fundamentals-source.md).

The portal is an Oracle ADF application with no API, so the collector drives
it in a browser. It runs the search, opens each filing, and writes what the
page shows to one JSON document per filing (`pqt.ids-filing/1`):

- the listing's columns;
- the form's fiscal year and quarter;
- each statement tab's column headings and cells, as text.

It parses no value and decides no period. That is the importer's job, and it
is tested there.

## Setup

It drives an **installed** browser (Edge by default, `--channel chrome` for
Chrome), so Playwright downloads nothing.

```bash
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt
```

## Run

```bash
.venv/Scripts/python -X utf8 collect.py --ticker FPT VCB --from-year 2022 --out ../../data/fundamentals/ids
```

- **Resumable.** A filing whose document exists is skipped, so an interrupted
  run continues where it stopped.
- **Paced.** Pages are read one at a time, `--pause` seconds apart (default
  1.5). It is a public service; read it at a person's pace.
- **One year per search.** A year with more than one page of filings is split
  in half until each search fits a page, which keeps away from the portal's
  pager.
- **Composed text.** The portal serves some Vietnamese decomposed. Everything
  is written in NFC.

`data/fundamentals/` is git-ignored. The documents are real disclosures,
retained for personal research only and never committed or redistributed.

## Import

The backend reads the directory named by `MARKET_DATA_IDS_FILINGS` (in the
container, `/app/data/fundamentals/ids`):

```bash
docker compose exec backend dotnet cli/PersonalQuant.Cli.dll fundamentals import --provider IDS --ticker FPT
```

Each run records new filings and re-reads held ones for changed values. It
reports late and refused filings.
