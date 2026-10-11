"""What the worker runs, by job kind.

A handler takes a job's payload and returns a :class:`JobOutcome`: a
JSON-serialisable result and, for research work, the run to record in the
experiment store (U9). The payload is validated here even though the backend
validated it on the way in: ``research.jobs`` is writable by the research
role, and a row is not trustworthy for having passed through one door. A
payload a handler cannot use raises :class:`JobRejectedError`, which fails the
job with that message.

The kinds are the ones the backend queues (``ResearchJobService``); a job of
any other kind fails as having no handler.
"""

from __future__ import annotations

import re
from collections.abc import Callable, Mapping
from dataclasses import dataclass, replace
from datetime import UTC, datetime
from pathlib import Path
from typing import Any, Protocol
from uuid import UUID

from personal_quant.factors.momentum import Momentum
from personal_quant.research.datasets import LocalDatasetProvider
from personal_quant.research.experiments import (
    RecordedRun,
    RunRecord,
    code_commit,
    compare_metrics,
    slice_metrics,
)
from personal_quant.research.momentum_slice import SliceResult, run_momentum_slice

__all__ = [
    "Handler",
    "JobOutcome",
    "JobRejectedError",
    "RunReader",
    "default_handlers",
    "describe",
    "momentum_slice_handler",
    "reproduce_run_handler",
]

_DATASET_ID = re.compile(r"[0-9a-f]{16}")
_EXPERIMENT = re.compile(r"[a-z0-9][a-z0-9_\-./]{0,127}")
_MOMENTUM_FIELDS = frozenset(
    {"dataset_id", "version", "quantiles", "rebalance_every", "experiment"}
)
_MAX_QUANTILES = 100
_MAX_REBALANCE = 252

MOMENTUM_SLICE = "momentum_slice"
REPRODUCE_RUN = "reproduce_run"


@dataclass(frozen=True, slots=True)
class JobOutcome:
    """What a handler produced: the job's result, and the run to record."""

    result: Mapping[str, Any]
    run: RunRecord | None = None


Handler = Callable[[Mapping[str, Any]], JobOutcome]


class JobRejectedError(ValueError):
    """A job's payload cannot be run; the message says why."""


class RunReader(Protocol):
    """Reads a recorded run, for re-execution."""

    def load(self, run_id: UUID) -> RecordedRun | None:
        """Return the run, or ``None`` when no such run is recorded."""
        ...


def momentum_slice_handler(dataset_root: Path) -> Handler:
    """Build the handler that runs the U6 vertical slice over an exported dataset.

    Args:
        dataset_root: The directory the backend exports datasets into.

    Returns:
        The handler.
    """
    provider = LocalDatasetProvider(dataset_root)

    def handle(payload: Mapping[str, Any]) -> JobOutcome:
        unknown = sorted(set(payload) - _MOMENTUM_FIELDS)

        if unknown:
            raise JobRejectedError(f"Unknown payload field(s): {', '.join(unknown)}.")

        dataset_id = payload.get("dataset_id")

        if not isinstance(dataset_id, str) or _DATASET_ID.fullmatch(dataset_id) is None:
            raise JobRejectedError(f"'{dataset_id}' is not a dataset id.")

        experiment = payload.get("experiment", MOMENTUM_SLICE)

        if not isinstance(experiment, str) or _EXPERIMENT.fullmatch(experiment) is None:
            raise JobRejectedError(f"'{experiment}' is not an experiment name.")

        version = None if payload.get("version") is None else _whole(payload, "version", 1, 1, None)
        quantiles = _whole(payload, "quantiles", 10, 1, _MAX_QUANTILES)
        every = _whole(payload, "rebalance_every", 21, 1, _MAX_REBALANCE)

        started = datetime.now(UTC)
        dataset = provider.open(dataset_id, version)
        result = run_momentum_slice(dataset, quantiles=quantiles, rebalance_every=every)

        # The build actually read, never "latest": a run that cannot name its
        # input cannot be reproduced.
        parameters = {
            "dataset_id": dataset_id,
            "version": dataset.manifest.dataset_version,
            "quantiles": quantiles,
            "rebalance_every": every,
        }

        return JobOutcome(
            result=describe(result),
            run=_record(experiment, result, parameters, dataset.manifest.schema_version, started),
        )

    return handle


def reproduce_run_handler(dataset_root: Path, runs: RunReader) -> Handler:
    """Build the handler that re-executes a recorded run and diffs its metrics.

    Refuses when the run's input is gone or changed (a different content hash
    is a different dataset) or its factor version is not the one this code
    computes: either way the re-execution would not be a reproduction.
    Different code is allowed and reported, since finding out what a code
    change did to a result is a reason to reproduce.

    Args:
        dataset_root: The directory the backend exports datasets into.
        runs: Where the recorded run is read.

    Returns:
        The handler.
    """
    provider = LocalDatasetProvider(dataset_root)
    momentum = momentum_slice_handler(dataset_root)

    def handle(payload: Mapping[str, Any]) -> JobOutcome:
        if set(payload) != {"run_id"}:
            raise JobRejectedError("A reproduction's payload is exactly {'run_id': ...}.")

        try:
            run_id = UUID(str(payload["run_id"]))
        except ValueError:
            raise JobRejectedError(f"'{payload['run_id']}' is not a run id.") from None

        recorded = runs.load(run_id)

        if recorded is None:
            raise JobRejectedError(f"No research run {run_id} is recorded.")

        if recorded.strategy != MOMENTUM_SLICE:
            raise JobRejectedError(
                f"Run {run_id} ran '{recorded.strategy}', which cannot be reproduced."
            )

        current = _factor_version(Momentum())

        if recorded.factor_version != current:
            raise JobRejectedError(
                f"Run {run_id} used {recorded.factor_version}; this code computes {current}."
            )

        manifest = provider.open(recorded.dataset_id, recorded.dataset_version).manifest

        if manifest.content_hash != recorded.content_hash:
            raise JobRejectedError(
                f"{recorded.dataset_id} v{recorded.dataset_version} now hashes to "
                f"{manifest.content_hash}, not the {recorded.content_hash} the run read."
            )

        rerun = momentum({**recorded.parameters, "experiment": recorded.experiment})
        assert rerun.run is not None  # noqa: S101 - the momentum handler always records one

        differences = compare_metrics(recorded.metrics, rerun.run.metrics)
        now = code_commit()

        return JobOutcome(
            result={
                "reproduces": str(run_id),
                "identical": not differences,
                "differences": differences,
                "code_commit": {"recorded": recorded.commit, "now": now},
                "same_code": recorded.commit is not None and recorded.commit == now,
            },
            run=replace(rerun.run, reproduces_run_id=run_id),
        )

    return handle


def default_handlers(dataset_root: Path, runs: RunReader) -> dict[str, Handler]:
    """Return every handler the worker runs, by kind.

    Args:
        dataset_root: The directory the backend exports datasets into.
        runs: Where recorded runs are read, for reproductions.

    Returns:
        The handlers.
    """
    return {
        MOMENTUM_SLICE: momentum_slice_handler(dataset_root),
        REPRODUCE_RUN: reproduce_run_handler(dataset_root, runs),
    }


def describe(result: SliceResult) -> dict[str, Any]:
    """Render a slice result as JSON: provenance, summary and every period.

    Args:
        result: The slice's result.

    Returns:
        A JSON-serialisable mapping.
    """
    info = result.information

    return {
        "dataset_id": result.dataset_id,
        "dataset_version": result.dataset_version,
        "content_hash": result.content_hash,
        "membership": result.membership,
        "adjusted": result.adjusted,
        "announcement_policy": result.announcement_policy,
        "known_as_of": result.known_as_of,
        "factor": result.factor,
        "factor_version": result.factor_version,
        "quantiles": result.quantiles,
        "rebalance_every": result.rebalance_every,
        "information": {
            "periods": info.periods,
            "mean": info.mean,
            "deviation": info.deviation,
            "information_ratio": info.information_ratio,
            "t_statistic": info.t_statistic,
        },
        "periods": [
            {
                "start": period.start.isoformat(),
                "end": period.end.isoformat(),
                "members": period.members,
                "scored": period.scored,
                "information_coefficient": period.information_coefficient,
                "top": period.top,
                "spread": period.spread,
                "bucket_returns": list(period.bucket_returns),
            }
            for period in result.periods
        ],
    }


def _record(
    experiment: str,
    result: SliceResult,
    parameters: Mapping[str, Any],
    schema_version: int,
    started: datetime,
) -> RunRecord:
    return RunRecord(
        experiment=experiment,
        strategy=MOMENTUM_SLICE,
        universe_code=result.universe_code,
        universe_as_of=None,
        membership=result.membership,
        dataset_id=result.dataset_id,
        dataset_version=result.dataset_version,
        schema_version=schema_version,
        content_hash=result.content_hash,
        factor_version=f"{result.factor}@{result.factor_version}",
        parameters=parameters,
        metrics=slice_metrics(result),
        started_at=started,
        finished_at=datetime.now(UTC),
    )


def _factor_version(factor: Momentum) -> str:
    return f"{factor.name}@{factor.version}"


def _whole(
    payload: Mapping[str, Any],
    field: str,
    default: int,
    low: int,
    high: int | None,
) -> int:
    value = payload.get(field, default)

    # bool is an int in Python; true is not a quantile count.
    if isinstance(value, bool) or not isinstance(value, int):
        raise JobRejectedError(f"'{field}' must be a whole number, not {value!r}.")

    if value < low or (high is not None and value > high):
        bound = f"between {low} and {high}" if high is not None else f"at least {low}"
        raise JobRejectedError(f"'{field}' must be {bound}, not {value}.")

    return value
