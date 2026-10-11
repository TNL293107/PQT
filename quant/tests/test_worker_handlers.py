"""The momentum-slice handler: payload checked again, result written as JSON."""

from __future__ import annotations

import json
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from personal_quant.worker.handlers import JobRejectedError, momentum_slice_handler
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


def test_a_valid_payload_runs_the_slice_and_returns_json(tmp_path: Path) -> None:
    _export(tmp_path)

    result = momentum_slice_handler(tmp_path)({"dataset_id": DATASET, "quantiles": 5})

    # It must survive the trip into a jsonb column.
    decoded = json.loads(json.dumps(result))
    assert decoded["dataset_id"] == DATASET
    assert decoded["membership"] == "point_in_time"
    assert decoded["factor"] == "momentum_12_1"
    assert decoded["quantiles"] == 5
    assert decoded["periods"]
    assert decoded["information"]["periods"] == len(decoded["periods"])


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
