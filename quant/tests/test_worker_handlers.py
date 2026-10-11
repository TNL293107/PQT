"""The worker's handlers: payload checked again, run recorded, reproduction diffed."""

from __future__ import annotations

import json
from dataclasses import replace
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import pytest

from personal_quant.research.experiments import (
    RecordedRun,
    compare_metrics,
    parameters_hash,
)
from personal_quant.worker.handlers import (
    JobRejectedError,
    momentum_slice_handler,
    reproduce_run_handler,
)
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

DATASET = "0123456789abcdef"
SESSIONS = weekdays(date(2024, 1, 1), 300)
NAMES = [f"inst{index:02d}" for index in range(10)]


def _export(tmp_path: Path) -> None:
    bars = [
        SyntheticBar(
            name,
            session,
            (Decimal(100) * (Decimal(1) + Decimal(rank) / Decimal(2000)) ** step).quantize(
                Decimal("0.000001")
            ),
        )
        for rank, name in enumerate(NAMES)
        for step, session in enumerate(SESSIONS)
    ]
    write_dataset(
        tmp_path,
        SyntheticDataset(bars=bars, spells={name: [(SESSIONS[0], None)] for name in NAMES}),
    )


class _Runs:
    """Holds recorded runs in memory."""

    def __init__(self, *runs: RecordedRun) -> None:
        self.held = {run.run_id: run for run in runs}

    def load(self, run_id: UUID) -> RecordedRun | None:
        return self.held.get(run_id)


def _recorded(tmp_path: Path, **changes: Any) -> RecordedRun:  # noqa: ANN401
    outcome = momentum_slice_handler(tmp_path)({"dataset_id": DATASET, "quantiles": 5})
    assert outcome.run is not None
    run = RecordedRun(
        run_id=uuid4(),
        experiment=outcome.run.experiment,
        strategy=outcome.run.strategy,
        status="succeeded",
        dataset_id=outcome.run.dataset_id,
        dataset_version=outcome.run.dataset_version,
        content_hash=outcome.run.content_hash,
        factor_version=outcome.run.factor_version,
        parameters=outcome.run.parameters,
        metrics=outcome.run.metrics,
        commit=None,
    )
    return replace(run, **changes)


def test_a_valid_payload_runs_the_slice_and_returns_json(tmp_path: Path) -> None:
    _export(tmp_path)

    outcome = momentum_slice_handler(tmp_path)({"dataset_id": DATASET, "quantiles": 5})

    # It must survive the trip into a jsonb column.
    decoded = json.loads(json.dumps(outcome.result))
    assert decoded["dataset_id"] == DATASET
    assert decoded["membership"] == "point_in_time"
    assert decoded["factor"] == "momentum_12_1"
    assert decoded["periods"]


def test_the_run_names_the_build_it_read_not_latest(tmp_path: Path) -> None:
    # Queued without a version, the run must still record which build it
    # read; "latest" would name a different input once a new build exists.
    _export(tmp_path)

    outcome = momentum_slice_handler(tmp_path)({"dataset_id": DATASET, "experiment": "mom"})

    assert outcome.run is not None
    assert outcome.run.experiment == "mom"
    assert outcome.run.parameters == {
        "dataset_id": DATASET,
        "version": 1,
        "quantiles": 10,
        "rebalance_every": 21,
    }
    assert outcome.run.dataset_version == 1
    assert outcome.run.factor_version == "momentum_12_1@1"
    assert outcome.run.metrics["ic_mean"] is not None


@pytest.mark.parametrize(
    ("payload", "message"),
    [
        ({"dataset_id": "../escape"}, "not a dataset id"),
        ({"dataset_id": DATASET.upper()}, "not a dataset id"),
        ({}, "not a dataset id"),
        ({"dataset_id": DATASET, "quantiles": 0}, "quantiles"),
        ({"dataset_id": DATASET, "quantiles": True}, "whole number"),
        ({"dataset_id": DATASET, "rebalance_every": 253}, "rebalance_every"),
        ({"dataset_id": DATASET, "version": "3"}, "whole number"),
        ({"dataset_id": DATASET, "experiment": "Bad Name"}, "experiment name"),
        ({"dataset_id": DATASET, "script": "rm -rf /"}, "Unknown payload field"),
    ],
)
def test_a_payload_the_slice_cannot_use_is_rejected(
    tmp_path: Path, payload: dict[str, Any], message: str
) -> None:
    with pytest.raises(JobRejectedError, match=message):
        momentum_slice_handler(tmp_path)(payload)


def test_a_dataset_that_is_not_there_fails_the_job(tmp_path: Path) -> None:
    with pytest.raises(FileNotFoundError, match=DATASET):
        momentum_slice_handler(tmp_path)({"dataset_id": DATASET})


def test_a_reproduction_of_an_unchanged_run_is_identical(tmp_path: Path) -> None:
    _export(tmp_path)
    original = _recorded(tmp_path)

    outcome = reproduce_run_handler(tmp_path, _Runs(original))({"run_id": str(original.run_id)})

    assert outcome.result["identical"] is True
    assert outcome.result["differences"] == {}
    assert outcome.run is not None
    assert outcome.run.reproduces_run_id == original.run_id
    assert outcome.run.experiment == original.experiment


def test_a_reproduction_reports_every_metric_that_moved(tmp_path: Path) -> None:
    # Stands in for a code change that shifted the result: the recorded value
    # no longer comes back, and the reproduction says which and by how much.
    _export(tmp_path)
    original = _recorded(tmp_path)
    original = replace(original, metrics={**original.metrics, "ic_mean": 0.5})

    outcome = reproduce_run_handler(tmp_path, _Runs(original))({"run_id": str(original.run_id)})

    assert outcome.result["identical"] is False
    assert set(outcome.result["differences"]) == {"ic_mean"}
    assert outcome.result["differences"]["ic_mean"]["recorded"] == 0.5


def test_a_run_whose_dataset_changed_is_not_reproduced(tmp_path: Path) -> None:
    _export(tmp_path)
    original = _recorded(tmp_path, content_hash="0" * 64)

    with pytest.raises(JobRejectedError, match="hashes to"):
        reproduce_run_handler(tmp_path, _Runs(original))({"run_id": str(original.run_id)})


def test_a_run_of_another_factor_version_is_not_reproduced(tmp_path: Path) -> None:
    _export(tmp_path)
    original = _recorded(tmp_path, factor_version="momentum_12_1@0")

    with pytest.raises(JobRejectedError, match="this code computes"):
        reproduce_run_handler(tmp_path, _Runs(original))({"run_id": str(original.run_id)})


@pytest.mark.parametrize(
    ("payload", "message"),
    [
        ({}, "exactly"),
        ({"run_id": "nope"}, "not a run id"),
        ({"run_id": str(UUID(int=7))}, "No research run"),
    ],
)
def test_a_reproduction_payload_that_names_no_run_is_rejected(
    tmp_path: Path, payload: dict[str, Any], message: str
) -> None:
    with pytest.raises(JobRejectedError, match=message):
        reproduce_run_handler(tmp_path, _Runs())(payload)


def test_equal_parameters_hash_equal_whatever_their_order() -> None:
    assert parameters_hash({"a": 1, "b": 2}) == parameters_hash({"b": 2, "a": 1})
    assert parameters_hash({"a": 1}) != parameters_hash({"a": 2})


def test_metrics_compare_exactly_and_treat_missing_as_different() -> None:
    assert compare_metrics({"a": 0.1, "b": None}, {"a": 0.1, "b": None}) == {}
    assert compare_metrics({"a": 0.1}, {"a": 0.1 + 1e-17 + 1e-16}) != {}
    assert compare_metrics({"a": 0.1}, {}) == {"a": {"recorded": 0.1, "reproduced": None}}
