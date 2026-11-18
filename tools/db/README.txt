PQT database on the USB stick
=============================

These folders hold the PQT database (pg_dump), its roles and the dataset volume, so that any
machine with Docker can continue the work. They are private: they hold IDS figures, role
password hashes and possibly vendor lines. Never commit them or upload them to GitHub
(ADR-038, ADR-041).

1. EXPORT, on the machine that has the database (the laptop holds the sprint 9/10 DB)
   - Docker Desktop must be running.
   - The repo's .env must be present. The script reads only POSTGRES_USER and POSTGRES_DATABASE.
   - It works with the stack up or down, and never starts a second server on running data.

     powershell -ExecutionPolicy Bypass -File <USB>\PQT\db\export-db.ps1 -Repo D:\PQT\PQT

   This writes <USB>\PQT\db\<yyyyMMdd-HHmm>-<computer>\ with these files:
     pqt.dump, roles.sql, datasets.tgz, inventory.txt, SHA256SUMS.txt
   inventory.txt holds the latest migration and per-table row counts, and no figures.
   A migration later than 20261011 and many fundamentals rows mean it is the sprint 9/10 DB.

2. RESTORE, on another machine
   - Stop its stack first, with: docker compose down. Do NOT pass -v.
   - Dry run into throwaway volumes. This touches nothing real:
     powershell -ExecutionPolicy Bypass -File <USB>\PQT\db\restore-db.ps1 -From <USB>\PQT\db\<folder> -Repo E:\PQT -PgVolume pqt-try-pg -DatasetVolume pqt-try-ds
   - Real restore:
     powershell -ExecutionPolicy Bypass -File <USB>\PQT\db\restore-db.ps1 -From <USB>\PQT\db\<folder> -Repo E:\PQT -Replace
     -Replace first saves the old volumes as pre-restore-*.tgz in the same folder.
   - It ends with "Restored. The inventory matches the export."

Desktop test (2026-11-18): 20261118-2116-desktop-stale-backup is the desktop's OLD database, kept as a backup.
It has 18 migrations up to 20261011 and is NOT the sprint 9/10 DB. Exporting it and restoring it
into throwaway volumes matched the export, and the -Replace path was tested too.

Drive letters: the stick is H: on the desktop and D: on the laptop, where the repo is D:\PQT\PQT.
The scripts write next to themselves, so the letter does not matter.
  Laptop:  powershell -ExecutionPolicy Bypass -File D:\PQT\db\export-db.ps1
  -Repo defaults to the PQT folder beside db\, here D:\PQT\PQT. With no .env there, the
  script uses quant_user / personal_quant; pass -User and -Database if yours differ.

Laptop export (2026-11-18 21:19): 20261118-2119-desktop-mdlkh1h is THE sprint 9/10 DB. It has 22 migrations
up to 20261021043357_CorporateActionCoverage, 26,829 bars, 1,582 financial reports, 236,338
statement lines, 100 corporate actions, 18 research runs and 5 datasets. A dry-run restore on the
desktop matched its inventory.
