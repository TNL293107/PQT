"""The job queue's SQL, against real PostgreSQL.

The tables are built from ``fixtures/research_schema.sql``, the backend's own
definition (an integration test there fails if the two drift), inside a
throwaway schema that is dropped afterwards, so these tests never touch a real
``research`` schema. They run when ``PQT_TEST_DATABASE_URL`` names a database
to use — CI provides one — and are skipped, saying so, otherwise.
"""

from __future__ import annotations

import os
import re
import uuid
from collections.abc import Iterator
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any

import psycopg
import pytest
from psycopg import sql

from personal_quant.research.experiments import RunRecord
from personal_quant.worker.queue import LeaseLostError, PostgresJobQueue

DATABASE_URL = os.environ.get("PQT_TEST_DATABASE_URL", "")
FIXTURE = Path(__file__).parent / "fixtures" / "research_schema.sql"
LEASE = timedelta(minutes=5)

pytestmark = pytest.mark.skipif(
    not DATABASE_URL,
    reason="Set PQT_TEST_DATABASE_URL to a PostgreSQL database to test the queue's SQL.",
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


@pytest.fixture
def connection() -> Iterator[psycopg.Connection[Any]]:
    """Return a connection for the queue under test."""
    with psycopg.connect(DATABASE_URL) as opened:
        yield opened


def _queue(schema: str, connection: psycopg.Connection[Any], *jobs: tuple[str, str]) -> None:
    for kind, created in jobs:
        connection.execute(
            sql.SQL(
                "INSERT INTO {} (id, kind, payload, status, created_at_utc) "
                "VALUES (gen_random_uuid(), %s, '{{}}'::jsonb, 'queued', %s::timestamptz)"
            ).format(sql.Identifier(schema, "jobs")),
            (kind, created),
        )

    connection.commit()


def _row(schema: str, connection: psycopg.Connection[Any], job_id: uuid.UUID) -> tuple[Any, ...]:
    row = connection.execute(
        sql.SQL(
            "SELECT status, claimed_by, attempts, result, error, finished_at_utc IS NOT NULL "
            "FROM {} WHERE id = %s"
        ).format(sql.Identifier(schema, "jobs")),
        (job_id,),
    ).fetchone()
    connection.commit()
    assert row is not None
    return tuple(row)


def test_the_oldest_queued_job_is_claimed_first(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    _queue(schema, connection, ("second", "2026-10-11T02:00Z"), ("first", "2026-10-11T01:00Z"))
    queue = PostgresJobQueue(connection, schema)

    job = queue.claim("w1", LEASE)

    assert job is not None
    assert (job.kind, job.attempts, dict(job.payload)) == ("first", 1, {})
    assert _row(schema, connection, job.id)[:3] == ("running", "w1", 1)


def test_two_workers_claim_different_jobs(schema: str, connection: psycopg.Connection[Any]) -> None:
    _queue(schema, connection, ("a", "2026-10-11T01:00Z"), ("b", "2026-10-11T02:00Z"))

    with psycopg.connect(DATABASE_URL) as other:
        first = PostgresJobQueue(connection, schema).claim("w1", LEASE)
        second = PostgresJobQueue(other, schema).claim("w2", LEASE)

    assert first is not None
    assert second is not None
    assert first.id != second.id


def test_a_running_job_under_its_lease_is_not_claimed_again(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    _queue(schema, connection, ("only", "2026-10-11T01:00Z"))
    queue = PostgresJobQueue(connection, schema)

    assert queue.claim("w1", LEASE) is not None
    assert queue.claim("w2", LEASE) is None


def test_a_lapsed_lease_makes_the_job_claimable_and_counts_the_attempt(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    # A worker that died holding the job: its lease ran out, the request did not.
    _queue(schema, connection, ("only", "2026-10-11T01:00Z"))
    queue = PostgresJobQueue(connection, schema)
    dead = queue.claim("w1", timedelta(seconds=-1))

    revived = queue.claim("w2", LEASE)

    assert dead is not None
    assert revived is not None
    assert revived.id == dead.id
    assert revived.attempts == 2
    assert _row(schema, connection, revived.id)[1] == "w2"


def test_a_completed_job_records_its_result(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    _queue(schema, connection, ("only", "2026-10-11T01:00Z"))
    queue = PostgresJobQueue(connection, schema)
    job = queue.claim("w1", LEASE)
    assert job is not None

    queue.complete(job, "w1", {"ic": 0.07, "periods": [1, 2]})

    status, _, _, result, error, finished = _row(schema, connection, job.id)
    assert (status, result, error, finished) == (
        "succeeded",
        {"ic": 0.07, "periods": [1, 2]},
        None,
        True,
    )


def test_a_failed_job_records_its_error(schema: str, connection: psycopg.Connection[Any]) -> None:
    _queue(schema, connection, ("only", "2026-10-11T01:00Z"))
    queue = PostgresJobQueue(connection, schema)
    job = queue.claim("w1", LEASE)
    assert job is not None

    queue.fail(job, "w1", "Rejected: no such dataset.")

    status, _, _, result, error, finished = _row(schema, connection, job.id)
    assert (status, result, error, finished) == ("failed", None, "Rejected: no such dataset.", True)


def test_a_failed_trial_records_its_run_as_failed_and_counts_it(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    # ADR-036 §5: a job that failed after its payload was accepted is a trial.
    _queue(schema, connection, ("only", "2026-10-11T01:00Z"))
    queue = PostgresJobQueue(connection, schema)
    job = queue.claim("w1", LEASE)
    assert job is not None

    queue.fail(job, "w1", "ZeroDivisionError: planted", _run(status="failed", metrics={}))

    status, _, _, _, error, _ = _row(schema, connection, job.id)
    assert (status, error) == ("failed", "ZeroDivisionError: planted")
    assert _trials(schema, connection) == [(1, 1, False)]

    runs = connection.execute(
        sql.SQL("SELECT status FROM {}").format(sql.Identifier(schema, "runs"))
    ).fetchall()
    connection.commit()
    assert [tuple(row) for row in runs] == [("failed",)]


def test_a_failed_reproduction_leaves_the_trial_count_unchanged(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    _queue(schema, connection, ("a", "2026-10-11T01:00Z"), ("b", "2026-10-11T02:00Z"))
    queue = PostgresJobQueue(connection, schema)
    original = _run()

    first = queue.claim("w1", LEASE)
    assert first is not None
    queue.complete(first, "w1", {"ok": True}, original)

    second = queue.claim("w1", LEASE)
    assert second is not None
    queue.fail(
        second,
        "w1",
        "ZeroDivisionError: planted",
        _run(status="failed", metrics={}, reproduces_run_id=original.run_id),
    )

    assert _trials(schema, connection) == [(1, 1, False), (None, None, True)]


def test_a_worker_that_lost_its_job_cannot_finish_it(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    # w1's lease lapsed and w2 holds the job now. w1 finishing late must not
    # overwrite whatever w2 records.
    _queue(schema, connection, ("only", "2026-10-11T01:00Z"))
    queue = PostgresJobQueue(connection, schema)
    stale = queue.claim("w1", timedelta(seconds=-1))
    assert stale is not None
    assert queue.claim("w2", LEASE) is not None

    with pytest.raises(LeaseLostError):
        queue.complete(stale, "w1", {"late": True})

    assert _row(schema, connection, stale.id)[:2] == ("running", "w2")


def _run(**changes: Any) -> RunRecord:  # noqa: ANN401
    record = RunRecord(
        experiment="momentum-test",
        strategy="momentum_slice",
        universe_code="VN30",
        universe_as_of=None,
        membership="point_in_time",
        dataset_id="0123456789abcdef",
        dataset_version=1,
        schema_version=2,
        content_hash="a" * 64,
        factor_version="momentum_12_1@1",
        parameters={"dataset_id": "0123456789abcdef", "version": 1},
        metrics={"ic_mean": 0.07, "t_statistic": None},
        started_at=datetime(2026, 10, 11, 1, 0, tzinfo=UTC),
        finished_at=datetime(2026, 10, 11, 1, 1, tzinfo=UTC),
    )
    return replace(record, **changes)


def _trials(schema: str, connection: psycopg.Connection[Any]) -> list[tuple[Any, ...]]:
    rows = connection.execute(
        sql.SQL(
            "SELECT trial_index, trial_count, reproduces_run_id IS NOT NULL FROM {} "
            "ORDER BY finished_at_utc, trial_index NULLS LAST"
        ).format(sql.Identifier(schema, "runs"))
    ).fetchall()
    connection.commit()
    return [tuple(row) for row in rows]


def test_a_completed_job_records_its_run_and_counts_the_trial(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    # The experiment is named by the run and created on first use; every
    # configuration after it is counted against it.
    _queue(schema, connection, ("a", "2026-10-11T01:00Z"), ("b", "2026-10-11T02:00Z"))
    queue = PostgresJobQueue(connection, schema)

    for _ in range(2):
        job = queue.claim("w1", LEASE)
        assert job is not None
        queue.complete(job, "w1", {"ok": True}, _run())

    assert _trials(schema, connection) == [(1, 1, False), (2, 2, False)]

    metrics = connection.execute(
        sql.SQL("SELECT name, value FROM {} ORDER BY name").format(
            sql.Identifier(schema, "run_metrics")
        )
    ).fetchall()
    connection.commit()
    assert [tuple(row) for row in metrics] == [
        ("ic_mean", 0.07),
        ("ic_mean", 0.07),
        ("t_statistic", None),
        ("t_statistic", None),
    ]


def test_the_result_names_the_run_it_recorded(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    _queue(schema, connection, ("a", "2026-10-11T01:00Z"))
    queue = PostgresJobQueue(connection, schema)
    job = queue.claim("w1", LEASE)
    assert job is not None
    run = _run()

    queue.complete(job, "w1", {"ok": True}, run)

    result = _row(schema, connection, job.id)[3]
    assert result == {"ok": True, "run": {"run_id": str(run.run_id), "trial_index": 1}}


def test_a_reproduction_is_recorded_without_counting_as_a_trial(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    _queue(schema, connection, ("a", "2026-10-11T01:00Z"), ("b", "2026-10-11T02:00Z"))
    queue = PostgresJobQueue(connection, schema)
    original = _run()

    first = queue.claim("w1", LEASE)
    assert first is not None
    queue.complete(first, "w1", {}, original)
    second = queue.claim("w1", LEASE)
    assert second is not None
    queue.complete(
        second,
        "w1",
        {},
        _run(reproduces_run_id=original.run_id, finished_at=datetime(2026, 10, 11, 2, tzinfo=UTC)),
    )

    assert _trials(schema, connection) == [(1, 1, False), (None, None, True)]


def test_a_job_whose_lease_was_lost_records_no_run(
    schema: str, connection: psycopg.Connection[Any]
) -> None:
    # The run and the job's outcome commit together or not at all; a late
    # worker must not leave a run behind a job someone else now holds.
    _queue(schema, connection, ("only", "2026-10-11T01:00Z"))
    queue = PostgresJobQueue(connection, schema)
    stale = queue.claim("w1", timedelta(seconds=-1))
    assert stale is not None
    assert queue.claim("w2", LEASE) is not None

    with pytest.raises(LeaseLostError):
        queue.complete(stale, "w1", {}, _run())

    assert _trials(schema, connection) == []

    # Nor when it fails: a failed trial is rolled back with the lost lease.
    with pytest.raises(LeaseLostError):
        queue.fail(stale, "w1", "late", _run(status="failed", metrics={}))

    assert _trials(schema, connection) == []
