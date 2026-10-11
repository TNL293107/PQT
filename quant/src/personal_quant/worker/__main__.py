"""Run the quant worker until it is told to stop.

Configuration comes from the environment, as everywhere in PQT:

* ``POSTGRES_HOST``, ``POSTGRES_PORT``, ``POSTGRES_DATABASE`` — where;
* ``RESEARCH_DB_PASSWORD`` — the ``quant_research`` role's password;
* ``PQT_DATASET_ROOT`` — the directory the backend exports datasets into;
* ``WORKER_POLL_SECONDS`` — the wait between polls of an empty queue (5).

A lost database connection is reconnected from with a growing delay rather
than ending the process; SIGTERM and SIGINT finish the current job and stop.
"""

from __future__ import annotations

import logging
import os
import signal
import socket
import threading
from pathlib import Path
from types import FrameType

import psycopg

from personal_quant.environment import MissingConfigurationError, load_research_settings
from personal_quant.worker.handlers import default_handlers
from personal_quant.worker.queue import PostgresJobQueue
from personal_quant.worker.runner import Worker

_log = logging.getLogger("personal_quant.worker")

_MAX_BACKOFF_SECONDS = 60.0


def main() -> int:
    """Run the worker.

    Returns:
        The exit code: 0 on a requested stop, 2 when configuration is missing.
    """
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")

    try:
        settings = load_research_settings()
    except MissingConfigurationError as error:
        _log.error("%s", error)
        return 2

    root = os.environ.get("PQT_DATASET_ROOT", "").strip()

    if not root:
        _log.error("PQT_DATASET_ROOT is not set: the worker cannot find the exported datasets.")
        return 2

    poll = float(os.environ.get("WORKER_POLL_SECONDS", "5") or 5)
    name = f"{socket.gethostname()}:{os.getpid()}"
    stop = threading.Event()

    def request_stop(signum: int, _frame: FrameType | None) -> None:
        _log.info("Signal %d received; stopping after the current job.", signum)
        stop.set()

    signal.signal(signal.SIGTERM, request_stop)
    signal.signal(signal.SIGINT, request_stop)

    handlers = default_handlers(Path(root))
    backoff = 1.0

    while not stop.is_set():
        try:
            with psycopg.connect(
                host=settings.host,
                port=settings.port,
                dbname=settings.database,
                user=settings.username,
                password=settings.password,
                application_name="pqt-quant-worker",
            ) as connection:
                _log.info("Worker %s connected as %s.", name, settings.username)
                backoff = 1.0
                Worker(PostgresJobQueue(connection), handlers, name).run_forever(stop, poll)
        except psycopg.OperationalError as error:
            _log.warning("Database unavailable (%s); retrying in %.0fs.", error, backoff)
            stop.wait(backoff)
            backoff = min(backoff * 2, _MAX_BACKOFF_SECONDS)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
