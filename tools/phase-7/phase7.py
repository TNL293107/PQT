"""The Phase 7 factor report, end to end: request, runs, tables.

Thin entry over ``personal_quant.research.factor_report``; see README.md.
Run from the repository root with the quant project's environment
(``uv run --project quant python tools/phase-7/phase7.py``):

    phase7.py request --to 2026-11-13 > req.json
    phase7.py runs --experiment phase-7-vn30-s11 > runs.jsonl
    phase7.py tables --experiment phase-7-vn30-s11 runs.jsonl
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import date
from pathlib import Path

import psycopg

from personal_quant.research.factor_report import (
    ReportError,
    build_request,
    read_runs,
    render_tables,
    runs_from_jsonl,
    runs_to_jsonl,
)


def main(argv: list[str] | None = None) -> int:
    """Run one step and return the exit code: 0 done, 1 refused (the reason on stderr)."""
    parser = argparse.ArgumentParser(prog="phase7", description=__doc__.splitlines()[0])
    commands = parser.add_subparsers(dest="command", required=True)

    request = commands.add_parser("request", help="print the labelled-dataset request (ADR-034)")
    request.add_argument("--universe", default="VN30")
    request.add_argument(
        "--from", dest="from_date", type=date.fromisoformat, default=date(2024, 8, 5)
    )
    request.add_argument("--to", dest="to_date", type=date.fromisoformat, required=True)

    runs = commands.add_parser("runs", help="read an experiment's runs read-only, as JSON lines")
    runs.add_argument("--experiment", required=True)
    runs.add_argument(
        "--database-url-env",
        default="PQT_DATABASE_URL",
        help="the environment variable holding the connection string",
    )

    tables = commands.add_parser("tables", help="render the report's tables from JSON lines")
    tables.add_argument("--experiment", required=True)
    tables.add_argument("runs_file", type=Path)

    arguments = parser.parse_args(argv)

    try:
        if arguments.command == "request":
            document = build_request(
                universe=arguments.universe,
                from_date=arguments.from_date,
                to_date=arguments.to_date,
            )
            sys.stdout.write(json.dumps(document, indent=2) + "\n")
        elif arguments.command == "runs":
            url = os.environ.get(arguments.database_url_env, "")

            if not url:
                parser.error(f"{arguments.database_url_env} is not set.")

            with psycopg.connect(url) as connection:
                sys.stdout.write(runs_to_jsonl(read_runs(connection, arguments.experiment)))
        else:
            text = arguments.runs_file.read_text(encoding="utf-8")
            sys.stdout.write(render_tables(runs_from_jsonl(text), arguments.experiment))
    except ReportError as error:
        sys.stderr.write(f"phase7: {error}\n")
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
