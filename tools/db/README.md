# Moving the PQT database between machines

PQT runs on two machines, a desktop and a laptop, and the database travels between them on a USB
stick. These scripts move it, and they keep **one live database at a time**.

| Script | What it does |
| --- | --- |
| `export-db.ps1` | Writes `pg_dump`, roles, the dataset volume, an inventory and checksums into a new folder in the stick's store |
| `restore-db.ps1` | Restores such a folder into this machine's Docker volumes, under the token's rules |
| `preflight.ps1` | Checks a machine before a restore, before `compose up`, and after it. It prints PASS/WARN/FAIL/SKIP only |
| `pqt-db-common.ps1` | Shared helpers. Dot-sourced, never run |

The **store** is the stick's `db` folder: `H:\PQT\db` on the desktop and `D:\PQT\db` on the laptop.
It must not be inside a git work tree, and the scripts refuse one. The dumps hold IDS figures, role
password hashes and, after the first live Vietcap import, vendor lines. Never commit them, and never
upload them anywhere (ADR-038, ADR-041). The scripts are in git; the dumps never are.

Windows PowerShell 5.1 or later, and Docker Desktop. Docker Desktop cannot bind-mount the stick, so
volumes move with `docker cp`, never with `-v <host path>`.

## The token: `HOLDER.txt`

`<store>\HOLDER.txt` says which machine holds the live database:

| `holder` | Meaning |
| --- | --- |
| `desktop` or `laptop` | That machine holds it, and only it may write: import, export, runs, migrations |
| `in-transit` | It was handed over. Only a restore of the folder named in the token, with a matching dump SHA-256, takes it |

The file also records the folder, its dump SHA-256, the Postgres image digest and the commit.

- **Export.**
  - A plain export is a snapshot (a rollback point), and only the holder may take one.
  - `-Handover` makes the token `in-transit`.
  - `-StaleBackup` exports from a machine that does not hold the database. The folder is marked
    `-stale-backup`, and a real restore refuses it.
  - `-InitHolder` writes the first token in an empty store.
- **Restore** into the project's volumes is allowed in two cases:
  - the token is `in-transit` and its SHA-256 matches `-From`'s `pqt.dump`;
  - this role holds the database and `-From` is one of its own snapshots (a rollback).

  Anything else is refused unless `-Force`. A dry run, into the throwaway volumes `-PgVolume` and
  `-DatasetVolume`, ignores the token and leaves it unchanged.
- **Folders** are named `<yyyyMMdd-HHmm>-<role>[-<note>]`, from `-Role`. They are never named after
  the computer, because the laptop's name starts with `DESKTOP-`.

## Moving the database

On the machine that holds it:

```powershell
powershell -ExecutionPolicy Bypass -File tools\db\preflight.ps1 -Role desktop -Store H:\PQT\db -Stage after-up
# Stop writers first: no job pending or running, no import running.
powershell -ExecutionPolicy Bypass -File tools\db\export-db.ps1 -Role desktop -Store H:\PQT\db -Handover
docker compose down          # never -v
# Then eject the stick ("Safely remove").
```

On the other machine:

```powershell
git pull
docker compose down          # never -v
powershell -ExecutionPolicy Bypass -File tools\db\preflight.ps1 -Role laptop -Store D:\PQT\db -Stage before-restore -From D:\PQT\db\<folder>
# Dry run into throwaway volumes: nothing real is touched.
powershell -ExecutionPolicy Bypass -File tools\db\restore-db.ps1 -Role laptop -From D:\PQT\db\<folder> -Repo D:\PQT\PQT -PgVolume pqt-try-pg -DatasetVolume pqt-try-ds
docker volume rm pqt-try-pg pqt-try-ds
# The real restore. It saves the old volumes in <store>\pre-restore\<stamp>\ first.
powershell -ExecutionPolicy Bypass -File tools\db\restore-db.ps1 -Role laptop -From D:\PQT\db\<folder> -Repo D:\PQT\PQT -Replace
powershell -ExecutionPolicy Bypass -File tools\db\preflight.ps1 -Role laptop -Store D:\PQT\db -Stage before-up
docker compose up -d postgres redis backend
powershell -ExecutionPolicy Bypass -File tools\db\preflight.ps1 -Role laptop -Store D:\PQT\db -Stage after-up
```

A restore ends with "Restored. The inventory matches the export." and makes `-Role` the holder.
Otherwise it exits 1 and leaves the token alone.

To take a snapshot without handing over, run `export-db.ps1 -Role <role> -Store <store> -Note s1`.

## What the inventory proves

`inventory.txt` holds three things:
- the latest migration;
- each table's row count;
- each table's **content hash**. That is the md5 of every row's text, in one fixed order and one
  fixed rendering (UTC, ISO dates, full float digits).

Two databases with the same counts but different rows therefore differ. A hash shows no value. A
folder exported before content hashes existed is compared on counts only, with a warning.

`meta.txt` holds the exporting machine's role, commit and Postgres image digest. A restore warns when
the image digest differs. A dump restores across minor versions, but not across a major one.

## Credentials

- `restore-db.ps1` reads `POSTGRES_USER`, `POSTGRES_DATABASE` and `POSTGRES_PASSWORD` from
  `<Repo>\.env`.
- Without a `.env`, pass `-User` and `-Database`. The password is then asked for at the console, so
  this works only when run by hand.
- The restored database keeps **this machine's** password for the bootstrap role.
- The backend re-provisions `quant_research` from this machine's `RESEARCH_DB_PASSWORD` when it
  starts, so the two machines' passwords need not match.
- `export-db.ps1` reads only the user and database names.

Each machine has its own `.env`, written by hand. It never travels on the stick. Set
`MARKET_DATA_IMPORT_ON_STARTUP=false`: otherwise every `compose up` writes reference data, and
inventories stop comparing. Preflight fails on it.

## Preflight checks

| Id | Check |
| --- | --- |
| A2–A6 | Docker answers. The Postgres image is present, with its digest compared to the token's. The .NET SDK that `global.json` asks for is installed. `quant\.venv` imports `personal_quant`. `frontend\node_modules` is present |
| B1–B6 | The tree is clean, and HEAD contains the commit the database was written with. Not behind `origin/develop`. `73588e3` is not an ancestor. On a removable drive, `.env` `COMPOSE_FILE` includes `docker-compose.portable.yml`. `.env` has the Postgres keys and `RESEARCH_DB_PASSWORD`, and `MARKET_DATA_IMPORT_ON_STARTUP` is false. Nothing under `tmp/` or `data/tmp/` is tracked |
| C1–C3 | The project's volumes exist. No container uses them before a restore. The host ports are free before `up` |
| D1 | `HOLDER.txt` names this role. When the token is in transit, `-From` must be its folder |
| D2 | After `up`, the live inventory against the token's folder. Equal is PASS. A changed table is WARN (expected only if this machine wrote). Fewer rows or an older migration is FAIL |
| D3, D4 | `/health` answers 200. `/app/data/fixtures` in the backend is not empty. `schema status` has no pending migration |
| D5 | With the portable override, the files in `<project>_pqt-data` match its `SHA256SUMS`, and `SHA256SUMS` equals the checkout's `data/fixtures` and `data/schemas` |
| E1, E2 | `-From`'s checksums check out. The stick has room for two more exports |

The seed manifest is `SHA256SUMS` at the root of the volume, written by `tools/stack/seed-data.ps1`
in `sha256sum -c` format. It has one line per file under `data/fixtures` and `data/schemas`, in the
form `<sha256>  <path relative to data/, forward slashes>`. Line order does not matter to D5.

## Keeping folders

- Keep the last three hand-over exports, one milestone export per closed live step, and the
  `pre-restore\<stamp>\` archives until the next hand-over passes.
- `20261118-2116-desktop-stale-backup` is the desktop's database before the sprint 9/10 one was
  restored. It is deleted after the rehearsal passes. That needs the Moderator.

## If the stick is lost

The stick holds the dumps, `roles.sql` and the repository copy with `tmp/adr-038-verification/`
(real Vietcap JSON), unencrypted. The Moderator accepted that risk on 2026-11-18. If the stick is
lost:
- rotate `POSTGRES_PASSWORD` and `RESEARCH_DB_PASSWORD` on both machines;
- treat the Vietcap JSON as exposed.
