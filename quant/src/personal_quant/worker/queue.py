"""The research job queue: ``research.jobs``, claimed with a lease.

The backend owns the table and queues work into it (ADR-016); this side claims
a job, runs it and records how it ended. Every transition is one SQL statement
in its own transaction, so a worker that dies between two of them leaves the
row in a state another worker can read.

**Claiming.** The oldest job that is queued — or running under a lease that
has lapsed, because its worker died — is taken with ``FOR UPDATE SKIP LOCKED``,
so two workers polling at once take different jobs instead of queueing behind
each other's lock. Each claim counts an attempt.

**Finishing.** Completing or failing a job is conditional on still holding it:
same worker, still running. A worker whose lease lapsed and whose job was
claimed again by someone else must not overwrite the new holder's outcome; it
gets :class:`LeaseLostError` instead.
"""

from __future__ import annotations

import json
from collections.abc import Mapping
from dataclasses import dataclass
from datetime import timedelta
from typing import Any, Protocol
from uuid import UUID

import psycopg
from psycopg import sql
from psycopg.types.json import Jsonb

__all__ = ["Job", "JobQueue", "LeaseLostError", "PostgresJobQueue"]


class LeaseLostError(RuntimeError):
    """The worker no longer holds the job it tried to finish."""


@dataclass(frozen=True, slots=True)
class Job:
    """A claimed job."""

    id: UUID
    kind: str
    payload: Mapping[str, Any]
    attempts: int


class JobQueue(Protocol):
    """Claims jobs and records how they ended."""

    def claim(self, worker: str, lease: timedelta) -> Job | None:
        """Take the oldest claimable job, or return ``None`` when there is none."""
        ...

    def complete(self, job: Job, worker: str, result: Mapping[str, Any]) -> None:
        """Record a job's result."""
        ...

    def fail(self, job: Job, worker: str, error: str) -> None:
        """Record why a job failed."""
        ...


class PostgresJobQueue:
    """The queue over ``research.jobs``, as the ``quant_research`` role sees it."""

    def __init__(self, connection: psycopg.Connection[Any], schema: str = "research") -> None:
        """Use a connection and the schema holding ``jobs``.

        Args:
            connection: A connection as ``quant_research``; transactions are
                managed here, one per statement.
            schema: The schema; ``research`` except in tests, which build a
                throwaway copy of the table from the backend's definition.
        """
        self._connection = connection
        self._table = sql.Identifier(schema, "jobs")

    def claim(self, worker: str, lease: timedelta) -> Job | None:
        """Take the oldest claimable job.

        Args:
            worker: This worker's name, recorded as the holder.
            lease: How long the claim lasts before another worker may take it.

        Returns:
            The job, or ``None`` when nothing is claimable.
        """
        statement = sql.SQL(
            """
            UPDATE {table}
            SET status = 'running',
                claimed_by = %(worker)s,
                claimed_at_utc = now(),
                lease_until_utc = now() + %(lease)s,
                attempts = attempts + 1
            WHERE id = (
                SELECT id FROM {table}
                WHERE status = 'queued'
                   OR (status = 'running' AND lease_until_utc < now())
                ORDER BY created_at_utc, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            RETURNING id, kind, payload, attempts
            """
        ).format(table=self._table)

        with self._connection.transaction(), self._connection.cursor() as cursor:
            cursor.execute(statement, {"worker": worker, "lease": lease})
            row = cursor.fetchone()

        if row is None:
            return None

        identifier, kind, payload, attempts = row

        return Job(
            id=identifier,
            kind=str(kind),
            payload=payload if isinstance(payload, Mapping) else json.loads(payload),
            attempts=int(attempts),
        )

    def complete(self, job: Job, worker: str, result: Mapping[str, Any]) -> None:
        """Record a job's result.

        Args:
            job: The job.
            worker: This worker's name.
            result: A JSON-serialisable result.

        Raises:
            LeaseLostError: The job is no longer this worker's.
        """
        self._finish(
            job,
            worker,
            sql.SQL("status = 'succeeded', result = %(result)s"),
            {"result": Jsonb(dict(result))},
        )

    def fail(self, job: Job, worker: str, error: str) -> None:
        """Record why a job failed.

        Args:
            job: The job.
            worker: This worker's name.
            error: What went wrong, for the operator.

        Raises:
            LeaseLostError: The job is no longer this worker's.
        """
        self._finish(job, worker, sql.SQL("status = 'failed', error = %(error)s"), {"error": error})

    def _finish(
        self,
        job: Job,
        worker: str,
        outcome: sql.Composable,
        parameters: dict[str, Any],
    ) -> None:
        statement = sql.SQL(
            """
            UPDATE {table}
            SET {outcome}, finished_at_utc = now(), lease_until_utc = NULL
            WHERE id = %(id)s AND claimed_by = %(worker)s AND status = 'running'
            """
        ).format(table=self._table, outcome=outcome)

        with self._connection.transaction(), self._connection.cursor() as cursor:
            cursor.execute(statement, {**parameters, "id": job.id, "worker": worker})
            finished = cursor.rowcount

        if finished != 1:
            raise LeaseLostError(
                f"Job {job.id} is no longer held by {worker}; its lease lapsed and it was "
                "claimed again, or it was finished elsewhere."
            )
