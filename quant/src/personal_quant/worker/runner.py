"""The worker loop: claim a job, run its handler, record how it ended.

A job's failure is recorded on the job, never raised out of the loop: one bad
payload or one crashing handler must not take the worker down with it, any
more than a failing experiment may take the API down (ADR-016). What can stop
the loop is the queue itself — a lost database connection — which the caller
reconnects from.

A job that has been claimed more than ``max_attempts`` times is failed without
being run. Each claim beyond the first means a worker died holding it, and a
job that kills its worker every time would otherwise do so forever.
"""

from __future__ import annotations

import logging
import threading
from collections.abc import Mapping
from datetime import timedelta

from personal_quant.research.experiments import RunRecord
from personal_quant.worker.handlers import Handler, JobRejectedError, TrialFailedError
from personal_quant.worker.queue import Job, JobQueue, LeaseLostError

__all__ = ["Worker"]

_log = logging.getLogger(__name__)

_DEFAULT_LEASE = timedelta(minutes=30)
_DEFAULT_ATTEMPTS = 3
_MAX_ERROR_LENGTH = 4000


class Worker:
    """Runs jobs from a queue, one at a time."""

    def __init__(
        self,
        queue: JobQueue,
        handlers: Mapping[str, Handler],
        name: str,
        *,
        lease: timedelta = _DEFAULT_LEASE,
        max_attempts: int = _DEFAULT_ATTEMPTS,
    ) -> None:
        """Set the worker up.

        Args:
            queue: Where jobs come from and outcomes go.
            handlers: The handler for each job kind.
            name: Recorded as the holder of every job claimed.
            lease: How long a claim lasts. Longer than any job runs: a lease
                that lapses mid-run lets a second worker start the same job.
            max_attempts: Claims after which a job is failed without running.
        """
        self._queue = queue
        self._handlers = dict(handlers)
        self._name = name
        self._lease = lease
        self._max_attempts = max_attempts

    def run_once(self) -> bool:
        """Claim and run one job.

        Returns:
            ``True`` when a job was claimed, ``False`` when the queue was empty.
        """
        job = self._queue.claim(self._name, self._lease)

        if job is None:
            return False

        _log.info("Claimed job %s (%s), attempt %d.", job.id, job.kind, job.attempts)
        self._settle(job)
        return True

    def run_forever(self, stop: threading.Event, poll_seconds: float = 5.0) -> None:
        """Run jobs until ``stop`` is set, polling when the queue is empty.

        Args:
            stop: Set to finish after the current job.
            poll_seconds: How long to wait between polls of an empty queue.
        """
        while not stop.is_set():
            if not self.run_once():
                stop.wait(poll_seconds)

    def _settle(self, job: Job) -> None:
        if job.attempts > self._max_attempts:
            self._fail(
                job,
                f"Abandoned after {job.attempts - 1} attempts: the worker holding it stopped "
                "each time without finishing it.",
            )
            return

        handler = self._handlers.get(job.kind)

        if handler is None:
            self._fail(job, f"No handler for job kind '{job.kind}'.")
            return

        try:
            outcome = handler(job.payload)
        except JobRejectedError as rejection:
            self._fail(job, f"Rejected: {rejection}")
            return
        except TrialFailedError as failure:
            _log.exception("Job %s failed as a trial.", job.id)
            self._fail(job, str(failure), failure.run)
            return
        except Exception as error:
            _log.exception("Job %s failed.", job.id)
            self._fail(job, f"{type(error).__name__}: {error}")
            return

        try:
            self._queue.complete(job, self._name, outcome.result, outcome.run)
        except LeaseLostError:
            _log.warning("Job %s finished after its lease was lost; result discarded.", job.id)
            return

        _log.info("Job %s succeeded.", job.id)

    def _fail(self, job: Job, error: str, run: RunRecord | None = None) -> None:
        try:
            self._queue.fail(job, self._name, error[:_MAX_ERROR_LENGTH], run)
        except LeaseLostError:
            _log.warning("Job %s failed after its lease was lost; error discarded.", job.id)
            return

        _log.info("Job %s failed: %s", job.id, error)
