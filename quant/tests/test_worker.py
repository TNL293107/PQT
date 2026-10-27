"""The worker loop, over a queue held in memory."""

from __future__ import annotations

from collections.abc import Mapping
from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import uuid4

import pytest

from personal_quant.research.experiments import RunRecord
from personal_quant.worker.handlers import JobOutcome, JobRejectedError, TrialFailedError
from personal_quant.worker.queue import Job, LeaseLostError
from personal_quant.worker.runner import Worker


class _Queue:
    """Hands out the jobs it was given and records how each ended."""

    def __init__(self, *jobs: Job, lose_lease: bool = False) -> None:
        self.pending = list(jobs)
        self.completed: dict[str, Mapping[str, Any]] = {}
        self.failed: dict[str, str] = {}
        self.failed_runs: dict[str, RunRecord] = {}
        self.lose_lease = lose_lease

    def claim(self, worker: str, lease: timedelta) -> Job | None:
        return self.pending.pop(0) if self.pending else None

    def complete(
        self, job: Job, worker: str, result: Mapping[str, Any], run: RunRecord | None = None
    ) -> None:
        if self.lose_lease:
            raise LeaseLostError("lost")

        self.completed[job.kind] = result

    def fail(self, job: Job, worker: str, error: str, run: RunRecord | None = None) -> None:
        if self.lose_lease:
            raise LeaseLostError("lost")

        self.failed[job.kind] = error

        if run is not None:
            self.failed_runs[job.kind] = run


def _job(kind: str = "echo", attempts: int = 1, **payload: Any) -> Job:  # noqa: ANN401
    return Job(id=uuid4(), kind=kind, payload=payload, attempts=attempts)


def _echo(payload: Mapping[str, Any]) -> JobOutcome:
    return JobOutcome({"echo": dict(payload)})


def _crash(payload: Mapping[str, Any]) -> JobOutcome:
    raise ZeroDivisionError("division by zero")


def _reject(payload: Mapping[str, Any]) -> JobOutcome:
    raise JobRejectedError("'quantiles' must be at least 1, not 0.")


def test_a_job_that_runs_is_completed_with_its_result() -> None:
    queue = _Queue(_job(value=1))

    assert Worker(queue, {"echo": _echo}, "w").run_once() is True
    assert queue.completed == {"echo": {"echo": {"value": 1}}}


def test_an_empty_queue_reports_that_nothing_was_claimed() -> None:
    assert Worker(_Queue(), {"echo": _echo}, "w").run_once() is False


def test_a_crashing_handler_fails_its_job_and_not_the_worker() -> None:
    # The isolation ADR-016 asks for, inside the process as well as around it.
    queue = _Queue(_job("crash"), _job("echo"))
    worker = Worker(queue, {"crash": _crash, "echo": _echo}, "w")

    worker.run_once()
    worker.run_once()

    assert queue.failed == {"crash": "ZeroDivisionError: division by zero"}
    assert "echo" in queue.completed


def test_a_rejected_payload_fails_with_the_reason() -> None:
    queue = _Queue(_job("reject"))

    Worker(queue, {"reject": _reject}, "w").run_once()

    assert queue.failed["reject"].startswith("Rejected: 'quantiles' must be at least 1")


def _failed_run() -> RunRecord:
    return RunRecord(
        experiment="evaluation",
        strategy="factor_evaluation",
        universe_code="TEST30",
        universe_as_of=None,
        membership="point_in_time",
        dataset_id="0123456789abcdef",
        dataset_version=1,
        schema_version=5,
        content_hash="a" * 64,
        factor_version="crashes@1",
        parameters={},
        metrics={},
        started_at=datetime(2026, 10, 27, tzinfo=UTC),
        finished_at=datetime(2026, 10, 27, tzinfo=UTC),
        status="failed",
    )


def test_a_trial_that_fails_is_recorded_with_its_failed_run() -> None:
    run = _failed_run()

    def fails(payload: Mapping[str, Any]) -> JobOutcome:
        raise TrialFailedError(ZeroDivisionError("planted"), run)

    queue = _Queue(_job("trial"))

    Worker(queue, {"trial": fails}, "w").run_once()

    assert queue.failed["trial"] == "ZeroDivisionError: planted"
    assert queue.failed_runs["trial"] is run
    assert queue.failed_runs["trial"].status == "failed"


def test_a_rejected_payload_records_no_run() -> None:
    queue = _Queue(_job("reject"))

    Worker(queue, {"reject": _reject}, "w").run_once()

    assert "reject" in queue.failed
    assert queue.failed_runs == {}


def test_a_kind_without_a_handler_fails() -> None:
    queue = _Queue(_job("unheard_of"))

    Worker(queue, {"echo": _echo}, "w").run_once()

    assert "No handler" in queue.failed["unheard_of"]


def test_a_job_that_keeps_killing_its_worker_is_abandoned_unrun() -> None:
    # A fourth claim means three workers died holding it. Running it again
    # would be the fourth.
    ran: list[str] = []

    def record(payload: Mapping[str, Any]) -> JobOutcome:
        ran.append("ran")
        return JobOutcome({})

    queue = _Queue(_job("echo", attempts=4))

    Worker(queue, {"echo": record}, "w", max_attempts=3).run_once()

    assert ran == []
    assert "Abandoned after 3 attempts" in queue.failed["echo"]


def test_losing_the_lease_discards_the_outcome_without_stopping_the_worker() -> None:
    queue = _Queue(_job("echo"), _job("crash"), lose_lease=True)
    worker = Worker(queue, {"echo": _echo, "crash": _crash}, "w")

    assert worker.run_once() is True
    assert worker.run_once() is True
    assert queue.completed == {}
    assert queue.failed == {}


def test_a_long_error_is_cut_to_fit() -> None:
    def verbose(payload: Mapping[str, Any]) -> JobOutcome:
        raise RuntimeError("x" * 10_000)

    queue = _Queue(_job("verbose"))

    Worker(queue, {"verbose": verbose}, "w").run_once()

    assert len(queue.failed["verbose"]) <= 4000


@pytest.mark.parametrize("attempts", [1, 3])
def test_a_job_within_its_attempts_is_run(attempts: int) -> None:
    queue = _Queue(_job("echo", attempts=attempts))

    Worker(queue, {"echo": _echo}, "w", max_attempts=3).run_once()

    assert "echo" in queue.completed
