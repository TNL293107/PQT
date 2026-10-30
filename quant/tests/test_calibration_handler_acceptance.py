"""Sprint 11.1 acceptance for the calibration job (ADR-037 §5), written by QA + QC before the code.

Red until ``calibration_handler`` exists; done when these pass unedited. The
same pattern as the factor evaluation job (ADR-036 §5, verbatim): one run per
trial, every payload field counts, a failed job is a trial, a refused payload
is not, and ``reproduce_run`` re-executes it.

The contract pinned here, beyond the names ADR-037 gives:

* ``personal_quant.worker.handlers.calibration_handler(dataset_root, engines=None)``;
* ``CALIBRATION = "calibration"``, the job kind and the run's strategy;
* the payload is exactly ``PAYLOAD_FIELDS`` below, and ``cal-1`` is the only
  ``calibration_version`` v1 accepts;
* a run's metrics include ``observations`` (the predictions scored), ``brier``,
  ``gap``, ``bin_{i}_count`` for i in 1..3, ``ev_mean`` and ``ev_count``.

The build is 60 sessions, not the 15 of the evaluation tests: with 6 names in
3 quantiles, ``p_model`` needs 10 rebalances of known labels before it exists
(ADR-037's 20-observation floor), so a shorter build would leave every number
absent and prove nothing.
"""

from __future__ import annotations

from collections.abc import Mapping
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import pytest

from personal_quant.factors.momentum import Momentum
from personal_quant.research.datasets import LocalDatasetProvider
from personal_quant.research.experiments import RecordedRun, parameters_hash
from personal_quant.research.protocols import FeatureEngine, History, Lookback
from personal_quant.worker.handlers import Handler
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

SESSIONS = weekdays(date(2026, 1, 5), 60)
DATASET = "0a1b2c3d4e5f6a7b"
HORIZON = 3
# Rates that alternate in sign by session, so labels are both positive and
# negative and p_model is neither 0 nor 1.
RATES = {"aaa": -0.010, "bbb": -0.004, "ccc": 0.000, "ddd": 0.004, "eee": 0.008, "fff": 0.012}
TRAILING = Momentum(formation=HORIZON, skip=0)

PAYLOAD_FIELDS = frozenset(
    {
        "experiment",
        "dataset_id",
        "dataset_version",
        "feature",
        "transform_version",
        "quantiles",
        "calibration_version",
        "cost_model_version",
        "buy_fee",
        "sell_fee",
        "sell_tax",
        "spread",
        "slippage",
    }
)


class _Crashes:
    """Fails inside the job, after the payload was accepted."""

    name = "crashes"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        raise ZeroDivisionError("planted")


ENGINES: list[FeatureEngine] = [TRAILING, _Crashes()]


def _write(tmp_path: Path) -> None:
    bars = [
        SyntheticBar(
            name,
            session,
            # A zigzag around a drift: up on even sessions, down on odd ones.
            Decimal(repr(100 * (1 + rate) ** index * (1.02 if index % 2 == 0 else 0.99))),
        )
        for name, rate in RATES.items()
        for index, session in enumerate(SESSIONS)
    ]
    write_dataset(
        tmp_path,
        SyntheticDataset(
            dataset_id=DATASET,
            bars=bars,
            spells={name: [(SESSIONS[0], None)] for name in RATES},
            request={
                "universe": "TEST30",
                "as_of": None,
                "from": SESSIONS[TRAILING.lookback.sessions - 1].isoformat(),
                "to": SESSIONS[-1].isoformat(),
                "interval": "1d",
                "fundamentals": False,
                "rebalance_every": HORIZON,
                "features": [
                    {"name": engine.name, "version": 1, "lookback": engine.lookback.sessions}
                    for engine in ENGINES
                ],
                "label": {"horizon": HORIZON, "basis": "adjusted"},
            },
        ),
    )


def _payload(**changes: object) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "experiment": "calibration-test",
        "dataset_id": DATASET,
        "dataset_version": 1,
        "feature": TRAILING.name,
        "transform_version": "raw",
        "quantiles": 3,
        "calibration_version": "cal-1",
        "cost_model_version": "v1",
        "buy_fee": 0.0015,
        "sell_fee": 0.0015,
        "sell_tax": 0.001,
        "spread": 0.001,
        "slippage": 0.0,
    }
    payload.update(changes)
    return payload


def _handler(tmp_path: Path) -> Handler:
    from personal_quant.worker.handlers import calibration_handler

    return calibration_handler(tmp_path, ENGINES)


class _Runs:
    def __init__(self, run: RecordedRun) -> None:
        self.run = run

    def load(self, run_id: UUID) -> RecordedRun | None:
        return self.run if run_id == self.run.run_id else None


# --- (a) the payload ---------------------------------------------------------


def test_the_payload_is_exactly_the_fields_adr_037_lists(tmp_path: Path) -> None:
    _write(tmp_path)

    outcome = _handler(tmp_path)(_payload())

    assert set(_payload()) == PAYLOAD_FIELDS
    assert outcome.run is not None
    # The run records every field but the experiment, as ADR-036 §5 does.
    assert set(outcome.run.parameters) == PAYLOAD_FIELDS - {"experiment"}


@pytest.mark.parametrize(
    ("changes", "message"),
    [
        ({"script": "rm -rf /"}, "Unknown payload field"),
        ({"calibration_version": "cal-9"}, "calibration_version"),
        ({"transform_version": "wz-9"}, "not a transform version"),
        ({"quantiles": 0}, "quantiles"),
        ({"buy_fee": -0.001}, "buy_fee"),
        ({"feature": "value_ep"}, "was not built for"),
    ],
)
def test_a_payload_the_job_cannot_use_is_refused_and_records_no_run(
    tmp_path: Path, changes: dict[str, Any], message: str
) -> None:
    from personal_quant.worker.handlers import JobRejectedError

    _write(tmp_path)

    # Refused before it runs: JobRejectedError carries no run, so nothing is
    # indexed (ADR-036 §5: a refused payload is not a trial).
    with pytest.raises(JobRejectedError, match=message):
        _handler(tmp_path)(_payload(**changes))


def test_a_payload_missing_a_field_is_refused(tmp_path: Path) -> None:
    from personal_quant.worker.handlers import JobRejectedError

    _write(tmp_path)
    payload = _payload()
    del payload["sell_tax"]

    with pytest.raises(JobRejectedError, match="Missing payload field"):
        _handler(tmp_path)(payload)


# --- (b) the run record ------------------------------------------------------


def test_the_run_is_recorded_under_its_own_experiment_as_a_trial(tmp_path: Path) -> None:
    from personal_quant.worker.handlers import CALIBRATION

    _write(tmp_path)

    outcome = _handler(tmp_path)(_payload())

    assert CALIBRATION == "calibration"
    assert outcome.run is not None
    assert outcome.run.strategy == "calibration"
    assert outcome.run.experiment == "calibration-test-calibration"
    # A trial: no reproduces_run_id, so the store gives it a trial_index.
    assert outcome.run.reproduces_run_id is None
    assert outcome.run.status == "succeeded"


# --- (c) a changed cost figure is a new trial --------------------------------


@pytest.mark.parametrize(
    "changes",
    [
        {"buy_fee": 0.002},
        {"sell_fee": 0.002},
        {"sell_tax": 0.002},
        {"spread": 0.002},
        {"slippage": 0.0005},
        {"cost_model_version": "v2"},
    ],
)
def test_a_changed_cost_figure_is_a_new_trial(tmp_path: Path, changes: dict[str, Any]) -> None:
    _write(tmp_path)

    base = _handler(tmp_path)(_payload())
    changed = _handler(tmp_path)(_payload(**changes))

    assert base.run is not None
    assert changed.run is not None
    assert parameters_hash(base.run.parameters) != parameters_hash(changed.run.parameters)


def test_the_same_payload_is_the_same_trial(tmp_path: Path) -> None:
    _write(tmp_path)

    first = _handler(tmp_path)(_payload())
    second = _handler(tmp_path)(_payload())

    assert first.run is not None
    assert second.run is not None
    assert parameters_hash(first.run.parameters) == parameters_hash(second.run.parameters)


# --- (d) reproduction --------------------------------------------------------


def test_reproduce_run_re_executes_a_calibration_run_identically(tmp_path: Path) -> None:
    from personal_quant.worker.handlers import reproduce_run_handler

    _write(tmp_path)
    original = _handler(tmp_path)(_payload())
    assert original.run is not None
    manifest = LocalDatasetProvider(tmp_path).open(DATASET).manifest
    recorded = RecordedRun(
        run_id=uuid4(),
        experiment=original.run.experiment,
        strategy=original.run.strategy,
        status="succeeded",
        dataset_id=DATASET,
        dataset_version=1,
        content_hash=manifest.content_hash,
        factor_version=original.run.factor_version,
        parameters=original.run.parameters,
        metrics=original.run.metrics,
        commit=None,
    )

    rerun = reproduce_run_handler(tmp_path, _Runs(recorded), ENGINES)(
        {"run_id": str(recorded.run_id)}
    )

    assert rerun.result["identical"] is True
    assert rerun.run is not None
    assert rerun.run.reproduces_run_id == recorded.run_id
    # Re-run under the experiment the run was recorded in, not a doubled suffix.
    assert rerun.run.experiment == "calibration-test-calibration"


# --- (e) a failed job is a trial ---------------------------------------------


def test_a_job_that_fails_after_its_payload_is_accepted_is_still_a_trial(tmp_path: Path) -> None:
    from personal_quant.worker.handlers import TrialFailedError

    _write(tmp_path)

    with pytest.raises(TrialFailedError, match="planted") as failure:
        _handler(tmp_path)(_payload(feature="crashes"))

    assert failure.value.run.status == "failed"
    assert failure.value.run.strategy == "calibration"
    assert failure.value.run.experiment == "calibration-test-calibration"
    assert failure.value.run.reproduces_run_id is None


# --- (f) every number carries its count --------------------------------------


def test_the_metrics_carry_their_observation_counts(tmp_path: Path) -> None:
    _write(tmp_path)

    outcome = _handler(tmp_path)(_payload())

    assert outcome.run is not None
    metrics = outcome.run.metrics

    for name in ("observations", "brier", "gap", "ev_mean", "ev_count"):
        assert name in metrics, name
    for index in (1, 2, 3):
        assert f"bin_{index}_count" in metrics

    observations = metrics["observations"]
    assert observations is not None
    assert observations > 0
    # The three bins partition the scored predictions.
    assert sum(metrics[f"bin_{index}_count"] or 0 for index in (1, 2, 3)) == observations
    # A Brier score exists exactly when something was scored.
    assert metrics["brier"] is not None
    assert metrics["ev_count"] is not None
    assert 0 < metrics["ev_count"] <= observations
