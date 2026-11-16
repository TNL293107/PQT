"""The factor report's read of the run store, against real PostgreSQL.

Built from ``fixtures/research_schema.sql`` in a throwaway schema, as the job
queue's tests are; skipped without ``PQT_TEST_DATABASE_URL``. Every run is
invented.
"""

from __future__ import annotations

import os
import re
import uuid
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import psycopg
import pytest
from psycopg import sql
from psycopg.types.json import Jsonb

from personal_quant.research.factor_report import read_runs

DATABASE_URL = os.environ.get("PQT_TEST_DATABASE_URL", "")
FIXTURE = Path(__file__).parent / "fixtures" / "research_schema.sql"

pytestmark = pytest.mark.skipif(
    not DATABASE_URL,
    reason="Set PQT_TEST_DATABASE_URL to a PostgreSQL database to test the report's SQL.",
)


@pytest.fixture
def schema() -> Iterator[str]:
    """Create a throwaway copy of the research schema and drop it afterwards."""
    name = f"research_test_{uuid.uuid4().hex[:12]}"
    ddl = re.sub(r"\bresearch(?=[.;])", name, FIXTURE.read_text(encoding="utf-8"))

    with psycopg.connect(DATABASE_URL, autocommit=True) as connection:
        connection.execute(ddl)

    yield name

    with psycopg.connect(DATABASE_URL, autocommit=True) as connection:
        connection.execute(sql.SQL("DROP SCHEMA {} CASCADE").format(sql.Identifier(name)))


def _experiment(connection: psycopg.Connection[Any], schema: str, name: str) -> uuid.UUID:
    experiment = uuid.uuid4()
    connection.execute(
        sql.SQL("INSERT INTO {} (id, name, created_at_utc) VALUES (%s, %s, now())").format(
            sql.Identifier(schema, "experiments")
        ),
        (experiment, name),
    )
    return experiment


def _run(
    connection: psycopg.Connection[Any],
    schema: str,
    experiment: uuid.UUID,
    *,
    trial: int | None,
    reproduces: uuid.UUID | None = None,
    metrics: dict[str, float | None] | None = None,
    started: str = "2026-11-01T00:00:00Z",
) -> uuid.UUID:
    run = uuid.uuid4()
    connection.execute(
        sql.SQL(
            "INSERT INTO {} (id, experiment_id, status, strategy, universe_code, membership, "
            "dataset_id, dataset_version, schema_version, content_hash, factor_version, params, "
            "params_hash, code_commit, research_engine, research_engine_version, trial_index, "
            "trial_count, reproduces_run_id, started_at_utc, finished_at_utc) VALUES (%s, %s, "
            "'succeeded', 'factor_evaluation', 'VN30', 'point_in_time', 'd0d0', 1, 6, %s, "
            "'size@1', %s, %s, 'c0ffee1', 'native', '1', %s, %s, %s, %s, %s)"
        ).format(sql.Identifier(schema, "runs")),
        (
            run,
            experiment,
            "a" * 64,
            Jsonb({"feature": "size", "transform": "raw"}),
            "h" * 64,
            trial,
            trial,
            reproduces,
            started,
            started,
        ),
    )

    for name, value in (metrics or {}).items():
        connection.execute(
            sql.SQL("INSERT INTO {} (run_id, name, split, value) VALUES (%s, %s, %s, %s)").format(
                sql.Identifier(schema, "run_metrics")
            ),
            (run, name, "all", value),
        )

    return run


def test_the_read_returns_both_experiments_in_trial_order_with_their_metrics(schema: str) -> None:
    with psycopg.connect(DATABASE_URL) as connection:
        main = _experiment(connection, schema, "p7")
        correlation = _experiment(connection, schema, "p7-correlation")
        other = _experiment(connection, schema, "p7-other")
        second = _run(connection, schema, main, trial=2, metrics={"rank_ic_mean": 0.1})
        first = _run(connection, schema, main, trial=1, metrics={"rank_ic_mean": None})
        _run(connection, schema, main, trial=None, reproduces=first)
        _run(connection, schema, correlation, trial=1, metrics={"corr_a__b_mean": 0.5})
        _run(connection, schema, other, trial=1)
        connection.commit()

        runs = read_runs(connection, "p7", schema)

    assert [(run.experiment, run.trial_index) for run in runs] == [
        ("p7", 1),
        ("p7", 2),
        ("p7", None),
        ("p7-correlation", 1),
    ]
    assert runs[0].run_id == str(first)
    assert runs[1].run_id == str(second)
    assert runs[0].metrics == {"rank_ic_mean": None}
    assert runs[2].reproduces_run_id == str(first)
    assert runs[3].metrics == {"corr_a__b_mean": 0.5}


def test_the_read_cannot_write(schema: str) -> None:
    # The transaction is read-only: a write slipped into it is refused.
    with psycopg.connect(DATABASE_URL) as connection:
        read_runs(connection, "p7", schema)
        connection.execute("BEGIN READ ONLY")

        with pytest.raises(psycopg.errors.ReadOnlySqlTransaction):
            _experiment(connection, schema, "p7")

        connection.rollback()
