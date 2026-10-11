"""What the worker runs, by job kind.

A handler takes a job's payload and returns a JSON-serialisable result. The
payload is validated here even though the backend validated it on the way in:
``research.jobs`` is writable by the research role, and a row is not
trustworthy for having passed through one door. A payload a handler cannot
use raises :class:`JobRejectedError`, which fails the job with that message.

The kinds are the ones the backend queues (``ResearchJobService``); a job of
any other kind fails as having no handler.
"""

from __future__ import annotations

import re
from collections.abc import Callable, Mapping
from pathlib import Path
from typing import Any

from personal_quant.research.datasets import LocalDatasetProvider
from personal_quant.research.momentum_slice import SliceResult, run_momentum_slice

__all__ = ["Handler", "JobRejectedError", "default_handlers", "momentum_slice_handler"]

Handler = Callable[[Mapping[str, Any]], Mapping[str, Any]]

_DATASET_ID = re.compile(r"[0-9a-f]{16}")
_MOMENTUM_FIELDS = frozenset({"dataset_id", "version", "quantiles", "rebalance_every"})
_MAX_QUANTILES = 100
_MAX_REBALANCE = 252


class JobRejectedError(ValueError):
    """A job's payload cannot be run; the message says why."""


def momentum_slice_handler(dataset_root: Path) -> Handler:
    """Build the handler that runs the U6 vertical slice over an exported dataset.

    Args:
        dataset_root: The directory the backend exports datasets into.

    Returns:
        The handler.
    """
    provider = LocalDatasetProvider(dataset_root)

    def handle(payload: Mapping[str, Any]) -> Mapping[str, Any]:
        unknown = sorted(set(payload) - _MOMENTUM_FIELDS)

        if unknown:
            raise JobRejectedError(f"Unknown payload field(s): {', '.join(unknown)}.")

        dataset_id = payload.get("dataset_id")

        if not isinstance(dataset_id, str) or _DATASET_ID.fullmatch(dataset_id) is None:
            raise JobRejectedError(f"'{dataset_id}' is not a dataset id.")

        version = None if payload.get("version") is None else _whole(payload, "version", 1, 1, None)
        quantiles = _whole(payload, "quantiles", 10, 1, _MAX_QUANTILES)
        every = _whole(payload, "rebalance_every", 21, 1, _MAX_REBALANCE)

        dataset = provider.open(dataset_id, version)
        result = run_momentum_slice(dataset, quantiles=quantiles, rebalance_every=every)

        return describe(result)

    return handle


def default_handlers(dataset_root: Path) -> dict[str, Handler]:
    """Return every handler the worker runs, by kind.

    Args:
        dataset_root: The directory the backend exports datasets into.

    Returns:
        The handlers.
    """
    return {"momentum_slice": momentum_slice_handler(dataset_root)}


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
