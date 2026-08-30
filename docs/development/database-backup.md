# Database backup and restore

A runbook, and a gate. **Take and verify a backup before the first ingest of
real market data**, and never treat a dump nobody has restored as a backup.

## Why this comes before real data

Everything in the database today can be regenerated. The fixtures are in the
repository, the reference data seeds itself, and a synthetic six-session series
can be re-imported in a second.

Real market data is the first thing here that cannot. A provider's answer for a
session that has already passed is not reproducible on demand: the endpoint may
be gone, the range may have moved out of a free window, and — worse for this
system specifically — the **observation history** in `quant.bar_revisions`
records what PQT believed and when. Re-fetching the same range next month does
not reconstruct it; it records a new observation at a new instant, and every
point-in-time read over the gap silently answers from the wrong side of it.

That is the whole reason U1 exists, and it is why the backup gate sits here
rather than at the first deployment.

## 1. Check the schema is current first

An ingest against a schema older than the observation history fails on the
missing table — after the provider call has already been spent, and with an
`IngestionRun` recorded as failed for a reason that reads like a provider
outage. Check before, not after.

```bash
dotnet ef migrations list --project backend/src/PersonalQuant.Infrastructure --startup-project backend/src/PersonalQuant.Infrastructure
```

Every migration must be listed without a `(Pending)` marker. At minimum the
database must be at `BarRevisions`; today's head is `UniverseCoverageFindings`.

Applying them, with `POSTGRES_PASSWORD` exported:

```bash
dotnet ef database update --project backend/src/PersonalQuant.Infrastructure --startup-project backend/src/PersonalQuant.Infrastructure
```

Under Compose this happens on start-up — `Postgres:ApplyMigrationsOnStartup` is
enabled there — so `docker compose up` is enough. The check is still worth
running: it is the difference between knowing and assuming.

## 2. Take the dump

The custom format (`-Fc`), because it restores selectively and compresses.
Timestamped, because an overwritten backup is one backup.

```bash
docker compose exec -T postgres pg_dump -U quant_user -d personal_quant -Fc > backup-$(date +%Y%m%d-%H%M).dump
```

Substitute the values from `.env` if they differ from the defaults. Keep the
file outside the repository: it will contain real market data, and
[`../architecture/data-policy.md`](../architecture/data-policy.md) governs where
that may live.

## 3. Restore it somewhere, and prove it

**This is the step that makes it a backup.** A dump that has never been
restored is a file with a hopeful name.

Restore into a scratch database beside the real one:

```bash
docker compose exec -T postgres createdb -U quant_user personal_quant_restore_check
```

```bash
docker compose exec -T postgres pg_restore -U quant_user -d personal_quant_restore_check --no-owner < backup-YYYYMMDD-HHMM.dump
```

Then compare what came back against what was taken. Row counts on the tables
that cannot be regenerated:

```bash
docker compose exec -T postgres psql -U quant_user -d personal_quant_restore_check -c "SELECT 'bars' AS relation, count(*) FROM quant.bars UNION ALL SELECT 'bar_revisions', count(*) FROM quant.bar_revisions UNION ALL SELECT 'raw_batches', count(*) FROM quant.market_data_raw_batches UNION ALL SELECT 'runs', count(*) FROM quant.ingestion_runs;"
```

Run the same query against `personal_quant` and compare. They must match
exactly. A restore that loses rows silently is the failure mode this drill
exists to catch, and it is not visible from the dump file.

Also confirm the restored database is at the same migration:

```bash
docker compose exec -T postgres psql -U quant_user -d personal_quant_restore_check -c "SELECT \"MigrationId\" FROM quant.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1;"
```

Drop the scratch database when the comparison passes:

```bash
docker compose exec -T postgres dropdb -U quant_user personal_quant_restore_check
```

## 4. What the Compose volume does and does not protect

`postgres-data` is a named Docker volume, so the data survives
`docker compose down` and a container rebuild. It does **not** survive
`docker compose down -v`, and it is not a backup: it is one copy, on one
machine, that a single flag removes.

```bash
docker compose down -v      # deletes postgres-data and redis-data
```

Treat that command as destructive on any database holding real market data.

## Cadence

| When | What |
| ---- | ---- |
| Before the first real-data ingest | Dump **and** restore-verify, per this runbook |
| Before any migration that alters an existing table | Dump; the restore drill is optional if one was verified recently |
| After a large backfill | Dump — a backfill is the largest irreproducible thing this system does |
| Routinely | Whatever the operator decides, verified at least once |

There is no automated backup here, and pretending otherwise would be worse than
the honest gap. Automating it belongs with the rest of the operational surface
in Phase 19.
