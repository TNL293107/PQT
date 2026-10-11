"""The experiment store, as the worker writes and reads it (U9).

Every finished research job leaves a run in ``research.runs``: what was read
(dataset, build, content hash), what ran (strategy, factor version, engine and
its version, code commit), with which parameters, and what it measured. The
target property is *same dataset version + same code commit + same
configuration ⇒ identical result*, and :func:`compare_metrics` is how a
reproduction checks it.

``trial_index`` and ``trial_count`` are statistical inputs, not bookkeeping:
the Deflated Sharpe Ratio and the Probability of Backtest Overfitting need the
number of configurations tried, and only a log holding every run can give it.
A run's index is assigned under a lock on its experiment's row, so two workers
finishing at once cannot take the same one. A reproduction is not a new
configuration: it records which run it re-executed and carries no index.
"""

from __future__ import annotations

import hashlib
import json
import math
import os
from collections.abc import Mapping
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any
from uuid import UUID, uuid4

import psycopg
from psycopg import sql
from psycopg.types.json import Jsonb

import personal_quant
from personal_quant.research.momentum_slice import SliceResult

__all__ = [
    "ENGINE",
    "RecordedRun",
    "RunRecord",
    "RunStore",
    "code_commit",
    "compare_metrics",
    "insert_run",
    "parameters_hash",
    "slice_metrics",
]

ENGINE = "personal_quant"


@dataclass(frozen=True, slots=True)
class RunRecord:
    """A run to record, built by the handler that produced it."""

    experiment: str
    strategy: str
    universe_code: str
    universe_as_of: str | None
    membership: str
    dataset_id: str
    dataset_version: int
    schema_version: int
    content_hash: str
    factor_version: str
    parameters: Mapping[str, Any]
    metrics: Mapping[str, float | None]
    started_at: datetime
    finished_at: datetime
    reproduces_run_id: UUID | None = None
    run_id: UUID = field(default_factory=uuid4)
    engine_version: str = personal_quant.__version__
    commit: str | None = field(default_factory=lambda: code_commit())


@dataclass(frozen=True, slots=True)
class RecordedRun:
    """A run as the store holds it, enough to re-execute it."""

    run_id: UUID
    experiment: str
    strategy: str
    status: str
    dataset_id: str
    dataset_version: int
    content_hash: str
    factor_version: str
    parameters: Mapping[str, Any]
    metrics: Mapping[str, float | None]
    commit: str | None


def code_commit() -> str | None:
    """Return the commit the worker was built from, when the build recorded it.

    Set as ``PQT_CODE_COMMIT`` at image build time. Absent, the run records
    ``None`` rather than a guess, and a reproduction says it cannot compare
    the code.

    Returns:
        The commit, or ``None``.
    """
    value = os.environ.get("PQT_CODE_COMMIT", "").strip()
    return value or None


def parameters_hash(parameters: Mapping[str, Any]) -> str:
    """Hash parameters canonically, so equal configurations hash equal.

    Args:
        parameters: The run's parameters.

    Returns:
        The lowercase hex SHA-256 of the sorted, compact JSON rendering.
    """
    canonical = json.dumps(parameters, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def slice_metrics(result: SliceResult) -> dict[str, float | None]:
    """Reduce a momentum-slice result to the metrics a run records.

    Args:
        result: The slice's result.

    Returns:
        The metrics by name.
    """
    info = result.information
    spread = [item.value for item in result.series("spread")]

    return {
        "periods": float(len(result.periods)),
        "ic_periods": float(info.periods),
        "ic_mean": info.mean,
        "ic_deviation": info.deviation,
        "information_ratio": info.information_ratio,
        "t_statistic": info.t_statistic,
        "top_compounded": _compound(result, "top"),
        "bottom_compounded": _compound(result, "bottom"),
        "spread_mean": sum(spread) / len(spread) if spread else None,
        "spread_positive_share": (
            sum(value > 0 for value in spread) / len(spread) if spread else None
        ),
    }


def compare_metrics(
    recorded: Mapping[str, float | None], reproduced: Mapping[str, float | None]
) -> dict[str, dict[str, float | None]]:
    """Return every metric whose value differs, exactly.

    No tolerance: the computation is deterministic, so a difference in the
    last bit is a difference in the input, the code or the platform, and that
    is what a reproduction exists to surface.

    Args:
        recorded: The original run's metrics.
        reproduced: The re-execution's metrics.

    Returns:
        The differing metrics, each with both values; empty when identical.
    """
    differences: dict[str, dict[str, float | None]] = {}

    for name in sorted(set(recorded) | set(reproduced)):
        before, after = recorded.get(name), reproduced.get(name)

        if not _same(before, after):
            differences[name] = {"recorded": before, "reproduced": after}

    return differences


def insert_run(
    cursor: psycopg.Cursor[Any], schema: str, job_id: UUID | None, run: RunRecord
) -> int | None:
    """Record a run, its metrics and its trial position, inside the caller's transaction.

    Args:
        cursor: A cursor in the transaction that also finishes the job.
        schema: The research schema.
        job_id: The job that produced the run.
        run: The run.

    Returns:
        The run's trial index, or ``None`` for a reproduction.
    """
    experiments = sql.Identifier(schema, "experiments")
    runs = sql.Identifier(schema, "runs")

    # The backend names experiments when it queues work; a job queued before
    # experiments existed names none, and gets its kind's.
    cursor.execute(
        sql.SQL(
            "INSERT INTO {} (id, name, created_at_utc) VALUES (%s, %s, now()) "
            "ON CONFLICT (name) DO NOTHING"
        ).format(experiments),
        (uuid4(), run.experiment),
    )
    cursor.execute(
        sql.SQL("SELECT id FROM {} WHERE name = %s FOR UPDATE").format(experiments),
        (run.experiment,),
    )
    row = cursor.fetchone()
    assert row is not None  # noqa: S101 - inserted or present a statement ago
    experiment_id = row[0]

    trial: int | None = None

    if run.reproduces_run_id is None:
        cursor.execute(
            sql.SQL(
                "SELECT coalesce(max(trial_index), 0) + 1 FROM {} WHERE experiment_id = %s"
            ).format(runs),
            (experiment_id,),
        )
        counted = cursor.fetchone()
        trial = int(counted[0]) if counted else 1

    cursor.execute(
        sql.SQL(
            """
            INSERT INTO {} (
                id, experiment_id, job_id, status, strategy, universe_code, universe_as_of,
                membership, dataset_id, dataset_version, schema_version, content_hash,
                factor_version, params, params_hash, code_commit, research_engine,
                research_engine_version, trial_index, trial_count, reproduces_run_id,
                started_at_utc, finished_at_utc)
            VALUES (%s, %s, %s, 'succeeded', %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s,
                    %s, %s, %s, %s, %s, %s, %s)
            """
        ).format(runs),
        (
            run.run_id,
            experiment_id,
            job_id,
            run.strategy,
            run.universe_code,
            run.universe_as_of,
            run.membership,
            run.dataset_id,
            run.dataset_version,
            run.schema_version,
            run.content_hash,
            run.factor_version,
            Jsonb(dict(run.parameters)),
            parameters_hash(run.parameters),
            run.commit,
            ENGINE,
            run.engine_version,
            trial,
            trial,
            run.reproduces_run_id,
            run.started_at,
            run.finished_at,
        ),
    )

    metrics = sql.Identifier(schema, "run_metrics")
    for name, value in sorted(run.metrics.items()):
        cursor.execute(
            sql.SQL(
                "INSERT INTO {} (run_id, name, split, value) VALUES (%s, %s, 'all', %s)"
            ).format(metrics),
            (run.run_id, name, value),
        )

    return trial


class RunStore:
    """Reads recorded runs, for re-execution."""

    def __init__(self, connection: psycopg.Connection[Any], schema: str = "research") -> None:
        """Use a connection and the research schema.

        Args:
            connection: A connection as ``quant_research``.
            schema: The research schema.
        """
        self._connection = connection
        self._schema = schema

    def load(self, run_id: UUID) -> RecordedRun | None:
        """Read a run and its metrics.

        Args:
            run_id: The run.

        Returns:
            The run, or ``None`` when no such run is recorded.
        """
        runs = sql.Identifier(self._schema, "runs")
        experiments = sql.Identifier(self._schema, "experiments")
        metrics = sql.Identifier(self._schema, "run_metrics")

        with self._connection.transaction(), self._connection.cursor() as cursor:
            cursor.execute(
                sql.SQL(
                    """
                    SELECT r.id, e.name, r.strategy, r.status, r.dataset_id, r.dataset_version,
                           r.content_hash, r.factor_version, r.params, r.code_commit
                    FROM {} r JOIN {} e ON e.id = r.experiment_id
                    WHERE r.id = %s
                    """
                ).format(runs, experiments),
                (run_id,),
            )
            row = cursor.fetchone()

            if row is None:
                return None

            cursor.execute(
                sql.SQL("SELECT name, value FROM {} WHERE run_id = %s AND split = 'all'").format(
                    metrics
                ),
                (run_id,),
            )
            measured = {str(name): value for name, value in cursor.fetchall()}

        return RecordedRun(
            run_id=row[0],
            experiment=str(row[1]),
            strategy=str(row[2]),
            status=str(row[3]),
            dataset_id=str(row[4]),
            dataset_version=int(row[5]),
            content_hash=str(row[6]),
            factor_version=str(row[7]),
            parameters=row[8] if isinstance(row[8], Mapping) else json.loads(row[8]),
            metrics=measured,
            commit=row[9],
        )


def _compound(result: SliceResult, which: str) -> float | None:
    series = result.series(which)

    if not series:
        return None

    return math.prod(1.0 + item.value for item in series) - 1.0


def _same(before: float | None, after: float | None) -> bool:
    if before is None or after is None:
        return before is after

    if math.isnan(before) and math.isnan(after):
        return True

    return before == after
