"""Whether Qlib is here, which Qlib it is, and the process-wide state it needs.

Qlib — and MLflow, which arrives with it — is imported here and in
:mod:`~personal_quant.integrations.qlib.model`, and nowhere else in PQT;
``tests/test_qlib_confinement.py`` fails the build on an import anywhere
outside this package. Even here the import is deferred
to the call that needs it, so importing the adapter costs nothing and works
without the ``research`` extra installed: :func:`available` answers the
question, and everything that needs the engine raises
:class:`QlibUnavailableError` with the install command rather than an
``ImportError`` from somewhere inside the call.

Qlib's model zoo logs training metrics through its Recorder, ``R``, which
exists only after ``qlib.init``. That initialisation is process-wide, so it
happens here, once, under the adapter's scratch directory:

* ``provider_uri`` is an empty directory inside the scratch. Qlib's data
  layer — its ``.bin`` store, providers and expression cache — is never read;
  PQT supplies features and labels itself (ADR-017). The directory exists only
  because ``qlib.init`` insists on one.
* MLflow, which the Recorder writes to, tracks into a SQLite file in the
  scratch, and its artifacts — the recorder saves the working directory's
  ``git`` status and diff with every run — land there too, never under the
  current directory. It is Qlib's sink, not PQT's experiment store; results
  leave the adapter as PQT values, and a run is recorded by PQT with
  :func:`engine_version` as its ``research_engine_version``.

The scratch is regenerable and never a source of truth. Unless a model is
given one, it is a directory private to the process — created on first use
with :func:`tempfile.mkdtemp`, so readable by its owner only, and removed when
the interpreter exits. It holds a diff of whatever working directory the
process ran in, which is nobody else's business, and a fixed shared path would
also let two processes write one SQLite file.

Single process, serialised fits. Qlib's configuration, its recorder ``R`` and
MLflow's active run are all process-global, so this module assumes one
process owns them and, within it, holds one lock across initialisation, the
experiment's creation and the whole of a recorded block: two threads fitting
at once take turns rather than start one recorder inside another. A process
forked after the first fit inherits the parent's Qlib configuration and
scratch, and should be given its own; a process started by
:mod:`multiprocessing`'s spawn or forkserver starts clean.
"""

from __future__ import annotations

import atexit
import gc
import importlib.util
import logging
import shutil
import sys
import tempfile
import threading
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path

__all__ = [
    "ENGINE",
    "INSTALL_HINT",
    "QlibUnavailableError",
    "available",
    "default_scratch",
    "engine_version",
    "recording",
]

ENGINE = "qlib"
"""The engine's name, as a run records it in ``research_engine``."""

INSTALL_HINT = 'pip install -e ".[research]"  (Python 3.12; see quant/README.md)'

_EXPERIMENT = "pqt-qlib-adapter"

_log = logging.getLogger(__name__)

# Held across initialisation and every recorded block: Qlib's configuration,
# its recorder and MLflow's active run belong to the process, not the caller.
_lock = threading.Lock()

# The scratch directory qlib.init was last given, or None before the first
# call. Qlib's configuration is process-wide; this mirrors it so that a second
# model with the same scratch does not re-initialise the engine under the
# first one's recorder. Read and written only under _lock.
_initialised: Path | None = None

# This process's default scratch, made on first use; None until then. Its own
# lock, so that asking for it never waits on a fit in another thread.
_default_scratch: Path | None = None
_default_scratch_lock = threading.Lock()


class QlibUnavailableError(ImportError):
    """Qlib is needed for this call and is not installed."""

    def __init__(self) -> None:
        """Name what is missing and how to install it."""
        super().__init__(
            f"Qlib is not installed; the adapter is optional (ADR-017). Install it with "
            f"{INSTALL_HINT}."
        )


def available() -> bool:
    """Return whether Qlib can be imported, without importing it.

    Returns:
        ``True`` when the ``research`` extra is installed.
    """
    return importlib.util.find_spec(ENGINE) is not None


def engine_version() -> str:
    """Return the installed Qlib's version, for ``research_engine_version``.

    Returns:
        The version Qlib reports for itself, ``0.9.7`` for example.

    Raises:
        QlibUnavailableError: Qlib is not installed.
    """
    if not available():
        raise QlibUnavailableError

    import qlib

    return str(qlib.__version__)


def default_scratch() -> Path:
    """Return the scratch directory used when a model is not given one.

    Made on the first call, once per process, and removed at interpreter exit.

    Returns:
        A ``pqt-qlib-*`` directory under the system's temporary directory,
        private to its owner.
    """
    global _default_scratch  # one per process, made on first use

    with _default_scratch_lock:
        if _default_scratch is None:
            _default_scratch = Path(tempfile.mkdtemp(prefix="pqt-qlib-"))
            atexit.register(_remove, _default_scratch)

        return _default_scratch


def _remove(scratch: Path) -> None:
    # At exit. A scratch that still cannot be removed is a private temporary
    # directory, and saying so is all that is left to do.
    def report(_function: object, path: str, error: BaseException) -> None:
        _log.warning("Could not remove Qlib scratch %s: %s", path, error)

    _close_databases_in(scratch)
    shutil.rmtree(scratch, onexc=report)


def _close_databases_in(scratch: Path) -> None:
    # MLflow keeps a SQLAlchemy engine per tracking URI for the life of the
    # process, in more than one private cache, and an engine's pooled
    # connections hold the SQLite file open — which Windows will not delete.
    # Engines are found through the garbage collector rather than MLflow's
    # internals, and only when SQLAlchemy is already loaded: nothing is
    # imported at exit.
    sqlalchemy = sys.modules.get("sqlalchemy")

    if sqlalchemy is None:
        return

    for candidate in gc.get_objects():
        if not isinstance(candidate, sqlalchemy.engine.Engine):
            continue

        database = candidate.url.database

        if database and Path(database).resolve().is_relative_to(scratch.resolve()):
            candidate.dispose()


@contextmanager
def recording(scratch: Path, run_name: str) -> Iterator[None]:
    """Run a block inside a Qlib recorder that writes only into the scratch.

    The process's Qlib lock is held for the whole block, so recorded blocks in
    different threads run one after another, never inside each other.

    Args:
        scratch: The adapter's scratch directory; created when absent.
        run_name: The recorder's name, so a run in the scratch can be found.

    Yields:
        Nothing; the block runs with ``R`` ready to record.

    Raises:
        QlibUnavailableError: Qlib is not installed.
    """
    if not available():
        raise QlibUnavailableError

    from qlib.workflow import R

    with _lock:
        _initialise(scratch)

        with R.start(experiment_name=_EXPERIMENT, recorder_name=run_name):
            yield


def _initialise(scratch: Path) -> None:
    # Called with _lock held.
    global _initialised  # mirrors Qlib's own process-wide configuration

    scratch = scratch.resolve()

    if _initialised == scratch:
        return

    import qlib
    from mlflow.tracking import MlflowClient

    provider = scratch / "unused-provider"
    provider.mkdir(parents=True, exist_ok=True)
    # MLflow 3 refuses its file-system store unless told otherwise; SQLite is
    # the backend it supports, and a file in the scratch is all it needs.
    # Not Path.as_uri() with its scheme swapped: that leaves three slashes
    # before an absolute POSIX path, which SQLAlchemy reads as relative. The
    # resolved path's POSIX form after "sqlite:///" gives the four slashes it
    # needs on POSIX and a drive path on Windows.
    tracking = f"sqlite:///{(scratch / 'mlflow.db').as_posix()}"

    # Created here, with its artifact location, because an experiment Qlib
    # creates puts artifacts — among them the recorder's `git diff` of the
    # working directory — under ./mlruns, wherever the process happens to run.
    client = MlflowClient(tracking_uri=tracking)

    if client.get_experiment_by_name(_EXPERIMENT) is None:
        client.create_experiment(_EXPERIMENT, artifact_location=(scratch / "artifacts").as_uri())

    qlib.init(
        provider_uri=str(provider),
        exp_manager={
            "class": "MLflowExpManager",
            "module_path": "qlib.workflow.expm",
            "kwargs": {"uri": tracking, "default_exp_name": _EXPERIMENT},
        },
    )
    _initialised = scratch
