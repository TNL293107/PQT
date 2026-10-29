"""Sprint 11.5 acceptance (QA + QC, before the code): the backend's payload is the worker's.

``fixtures/jobs/*.json`` is the one payload per kind. The .NET test
``ResearchJobQueueEvaluationTests`` asserts the backend queues exactly it, and
this file asserts the worker's handlers accept exactly it, so the two sides
cannot drift apart without one of them going red.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

from personal_quant.worker.handlers import (
    JobRejectedError,
    factor_correlation_handler,
    factor_evaluation_handler,
)
from test_evaluation_acceptance import DATASET, SOUND, _write

FIXTURES = Path(__file__).parent / "fixtures" / "jobs"


def _payload(kind: str) -> dict[str, Any]:
    payload: dict[str, Any] = json.loads((FIXTURES / f"{kind}.json").read_text(encoding="utf-8"))
    assert payload["dataset_id"] == DATASET  # the build the acceptance tests write
    return payload


def test_the_backend_s_factor_evaluation_payload_is_accepted_by_the_worker(tmp_path: Path) -> None:
    _write(tmp_path)

    outcome = factor_evaluation_handler(tmp_path, engines=SOUND)(_payload("factor_evaluation"))

    assert outcome.run is not None
    assert outcome.run.strategy == "factor_evaluation"
    assert outcome.run.experiment == "acceptance"


def test_the_backend_s_factor_correlation_payload_is_accepted_by_the_worker(tmp_path: Path) -> None:
    _write(tmp_path)

    outcome = factor_correlation_handler(tmp_path, engines=SOUND)(_payload("factor_correlation"))

    assert outcome.run is not None
    assert outcome.run.strategy == "factor_correlation"
    assert outcome.run.experiment == "acceptance-correlation"


@pytest.mark.parametrize("kind", ["factor_evaluation", "factor_correlation"])
def test_a_field_the_backend_does_not_write_is_refused_by_the_worker(
    tmp_path: Path, kind: str
) -> None:
    # Guards the fixture itself: a field added on one side only shows up here.
    _write(tmp_path)
    handler = (
        factor_evaluation_handler if kind == "factor_evaluation" else factor_correlation_handler
    )(tmp_path, engines=SOUND)

    with pytest.raises(JobRejectedError, match="Unknown payload field"):
        handler({**_payload(kind), "hypothesis": "not a payload field"})
