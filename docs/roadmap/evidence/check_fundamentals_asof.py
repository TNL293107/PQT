"""Check that no fundamentals row of an exported dataset uses anything published after its cut.

Phase 6 definition of done, criterion 7 (ADR-031 decision 2). Run from ``quant/``:

    .venv/Scripts/python -I ../docs/roadmap/evidence/check_fundamentals_asof.py \
        <export-root> <dataset-id> <reports.csv> <adjustments.csv>

``reports.csv`` and ``adjustments.csv`` are dumped from the database the export
was read from (the commands are in ``sprint-7-c-dataset.md``). The dataset is
opened through the verifying reader, so a build that fails verification fails
here too.

For every row, against the row's ``known_as_of_utc`` (its cut):

1. every report in ``report_ids`` was known by the cut;
2. ``balance_sheet_quarter`` is no later than the latest period of a report with
   values, of the row's scope, known by the cut;
3. ``ttm_last_quarter`` likewise (``ttm_first_quarter`` is three quarters before it);
4. ``actions_applied`` is no more than the share-count actions that went ex by the
   session and were announced by it, cancelled ones excluded;
5. ``known_as_of_utc`` is the session's 00:00 UTC (the export has no
   ``known_as_of``) and never later.

The close is the session's own, which ADR-030 allows: the ratio is what could be
computed after the session closed from what was published before it opened.
"""

from __future__ import annotations

import csv
import json
import sys
from collections import defaultdict
from datetime import UTC, date, datetime
from decimal import Decimal
from pathlib import Path

from personal_quant.research.datasets import LocalDatasetProvider

SCOPES = {"1": "separate", "2": "consolidated"}


def _quarter(text: str) -> tuple[int, int]:
    year, quarter = text.split("Q")
    return int(year), int(quarter)


def main(root: str, dataset_id: str, reports_csv: str, adjustments_csv: str) -> int:
    """Check every fundamentals row of one build; return 1 when any check fails."""
    build = LocalDatasetProvider(Path(root)).verify(dataset_id)
    table = build.fundamentals

    if table is None:
        print(f"dataset {dataset_id} carries no fundamentals")
        return 1

    known_from: dict[str, datetime] = {}
    periods: dict[tuple[str, str], list[tuple[datetime, tuple[int, int]]]] = defaultdict(list)

    with open(reports_csv, encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            instant = datetime.fromisoformat(row["known_from_utc"].replace(" ", "T")).astimezone(
                UTC
            )
            known_from[row["id"]] = instant

            if row["values_filed"] == "t":
                periods[(row["instrument_id"], SCOPES[row["scope"]])].append(
                    (instant, (int(row["fiscal_year"]), int(row["fiscal_quarter"])))
                )

    share_actions: dict[str, list[tuple[date, date | None]]] = defaultdict(list)

    with open(adjustments_csv, encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            if row["is_cancelled"] == "t" or Decimal(row["share_factor"]) == 1:
                continue

            announced = date.fromisoformat(row["announced_on"]) if row["announced_on"] else None
            share_actions[row["instrument_id"]].append(
                (date.fromisoformat(row["ex_date"]), announced)
            )

    columns = table.to_pydict()
    failures: list[str] = []
    checked = 0

    for index in range(table.num_rows):
        instrument = columns["instrument_id"][index]
        session: date = columns["session"][index]
        cut = columns["known_as_of_utc"][index].replace(tzinfo=UTC)
        scope = columns["scope"][index]
        where = f"{instrument} {session}"
        checked += 1

        opened = datetime(session.year, session.month, session.day, tzinfo=UTC)

        if cut != opened:
            failures.append(f"{where}: cut {cut} is not the session's 00:00 UTC")

        for report in json.loads(columns["report_ids"][index]):
            if known_from[report] > cut:
                failures.append(
                    f"{where}: report {report} known {known_from[report]}, after the cut"
                )

        latest = max(
            (period for instant, period in periods.get((instrument, scope), []) if instant <= cut),
            default=None,
        )

        for name in ("balance_sheet_quarter", "ttm_last_quarter"):
            value = columns[name][index]

            if value is not None and (latest is None or _quarter(value) > latest):
                failures.append(
                    f"{where}: {name} {value} is later than any report known by the cut ({latest})"
                )

        allowed = sum(
            1
            for ex_date, announced in share_actions.get(instrument, [])
            if ex_date <= session and announced is not None and announced <= session
        )

        if columns["actions_applied"][index] > allowed:
            failures.append(
                f"{where}: {columns['actions_applied'][index]} actions applied, {allowed} announced"
            )

    print(f"dataset {dataset_id} v{build.manifest.dataset_version}: {checked} rows checked")

    for failure in failures[:50]:
        print("LEAK", failure)

    print(f"{len(failures)} checks failed: a value uses something published after its cut")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))
