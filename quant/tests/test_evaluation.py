"""The factor evaluation job beyond the acceptance tests (ADR-036 §1, §2, §4, §5)."""

from __future__ import annotations

import math
from collections.abc import Mapping
from datetime import date
from decimal import Decimal
from pathlib import Path
from typing import Any
from uuid import UUID, uuid4

import pytest

from personal_quant.factors.momentum import Momentum
from personal_quant.research.datasets import LocalDatasetProvider
from personal_quant.research.experiments import RecordedRun
from personal_quant.research.protocols import FeatureEngine, History, Lookback
from personal_quant.worker.handlers import (
    JobRejectedError,
    TrialFailedError,
    factor_correlation_handler,
    factor_evaluation_handler,
    reproduce_run_handler,
)
from synthetic import SyntheticBar, SyntheticDataset, weekdays, write_dataset

SESSIONS = weekdays(date(2026, 1, 5), 15)
DATASET = "fedcba9876543210"
HORIZON = 3
RATES = {"aaa": 0.00, "bbb": 0.01, "ccc": 0.02, "ddd": 0.03, "eee": 0.04, "fff": 0.05}
TRAILING = Momentum(formation=HORIZON, skip=0)


class _Constant:
    """Ranks the names the same way at every rebalance: turnover 0."""

    name = "constant_rank"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        return {name: float(index) for index, name in enumerate(sorted(history.members()))}


class _Crashes:
    """Fails inside the evaluation, after the payload was accepted."""

    name = "crashes"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        raise ZeroDivisionError("planted")


class _Convex:
    """The trailing return cubed: the same ranks, so rank IC 1, but not linear in it."""

    name = "convex_momentum"
    version = "1"
    lookback = TRAILING.lookback

    def compute(self, history: History) -> Mapping[str, float]:
        return {name: value**3 for name, value in TRAILING.compute(history).items()}


ENGINES: list[FeatureEngine] = [TRAILING, _Constant(), _Crashes(), _Convex()]


def _write(tmp_path: Path, engines: list[Any]) -> None:
    bars = [
        SyntheticBar(name, session, Decimal(repr(100 * (1 + rate) ** index)))
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
                    for engine in engines
                ],
                "label": {"horizon": HORIZON, "basis": "adjusted"},
            },
        ),
    )


def _payload(**changes: object) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "experiment": "evaluation",
        "dataset_id": DATASET,
        "dataset_version": 1,
        "feature": TRAILING.name,
        "transform_version": "raw",
        "quantiles": 3,
        "decay_horizons": [HORIZON],
    }
    payload.update(changes)
    return payload


def test_turnover_is_zero_for_a_constant_ranking_and_absent_at_the_first_rebalance(
    tmp_path: Path,
) -> None:
    # Three rebalances: two turnover periods, each zero.
    _write(tmp_path, [_Constant()])

    outcome = factor_evaluation_handler(tmp_path, ENGINES)(_payload(feature="constant_rank"))

    assert outcome.run is not None
    assert outcome.run.metrics["turnover_periods"] == 2
    assert outcome.run.metrics["turnover_mean"] == 0.0


def test_the_run_records_the_payload_without_the_experiment_and_the_feature_version(
    tmp_path: Path,
) -> None:
    _write(tmp_path, [TRAILING])

    outcome = factor_evaluation_handler(tmp_path, ENGINES)(_payload())

    assert outcome.run is not None
    assert outcome.run.strategy == "factor_evaluation"
    assert outcome.run.factor_version == "momentum_3_0@1"
    assert outcome.run.status == "succeeded"
    assert outcome.run.parameters == {
        key: value for key, value in _payload().items() if key != "experiment"
    }
    assert outcome.run.metrics["spread_periods"] == 3
    spread = outcome.run.metrics["spread_mean"]
    assert spread is not None
    assert spread > 0


def test_a_trial_that_fails_after_its_payload_is_accepted_is_still_a_trial(tmp_path: Path) -> None:
    _write(tmp_path, [_Crashes()])

    with pytest.raises(TrialFailedError, match="planted") as failure:
        factor_evaluation_handler(tmp_path, ENGINES)(_payload(feature="crashes"))

    assert failure.value.run.status == "failed"
    assert failure.value.run.reproduces_run_id is None
    assert failure.value.run.factor_version == "crashes@1"


@pytest.mark.parametrize(
    ("changes", "message"),
    [
        ({"script": "rm -rf /"}, "Unknown payload field"),
        ({"feature": "value_ep"}, "was not built for"),
        ({"transform_version": "wz-9"}, "not a transform version"),
        ({"quantiles": 0}, "quantiles"),
        ({"decay_horizons": []}, "decay_horizons"),
        ({"decay_horizons": [0]}, "decay_horizons"),
        ({"dataset_id": "../escape"}, "not a dataset id"),
    ],
)
def test_a_payload_the_job_cannot_use_is_refused_before_it_is_a_trial(
    tmp_path: Path, changes: dict[str, Any], message: str
) -> None:
    _write(tmp_path, [TRAILING])

    with pytest.raises(JobRejectedError, match=message):
        factor_evaluation_handler(tmp_path, ENGINES)(_payload(**changes))


def test_a_payload_missing_a_field_is_refused(tmp_path: Path) -> None:
    payload = _payload()
    del payload["decay_horizons"]

    with pytest.raises(JobRejectedError, match="Missing payload field"):
        factor_evaluation_handler(tmp_path, ENGINES)(payload)


def test_factor_correlation_averages_per_rebalance_under_its_own_experiment(tmp_path: Path) -> None:
    # The trailing return and the constant rank both order the names by
    # rate, so their rank correlation is 1 at every rebalance.
    _write(tmp_path, [TRAILING, _Constant()])

    outcome = factor_correlation_handler(tmp_path, ENGINES)(
        {
            "experiment": "evaluation",
            "dataset_id": DATASET,
            "dataset_version": 1,
            "transform_version": "raw",
        }
    )

    assert outcome.run is not None
    assert outcome.run.experiment == "evaluation-correlation"
    assert outcome.run.strategy == "factor_correlation"
    assert outcome.run.metrics["corr_constant_rank__momentum_3_0_mean"] == pytest.approx(1.0)
    assert outcome.run.metrics["corr_constant_rank__momentum_3_0_periods"] == 3


def test_ic_is_pearson_and_rank_ic_is_spearman(tmp_path: Path) -> None:
    # A monotone, non-linear transform of the label keeps rank IC at 1 and
    # pulls the Pearson IC below it.
    _write(tmp_path, [_Convex()])

    outcome = factor_evaluation_handler(tmp_path, ENGINES)(_payload(feature="convex_momentum"))

    assert outcome.run is not None
    assert outcome.run.metrics["rank_ic_mean"] == pytest.approx(1.0)
    ic = outcome.run.metrics["ic_mean"]
    assert ic is not None
    assert ic < 1 - 1e-6


def test_the_metrics_read_the_transformed_feature(tmp_path: Path) -> None:
    # wz-1 z-scores the convex feature, and its Pearson IC moves with it.
    _write(tmp_path, [_Convex()])
    handler = factor_evaluation_handler(tmp_path, ENGINES)

    raw = handler(_payload(feature="convex_momentum", transform_version="raw")).run
    wz = handler(_payload(feature="convex_momentum", transform_version="wz-1")).run

    assert raw is not None
    assert wz is not None
    assert raw.metrics["ic_mean"] != wz.metrics["ic_mean"]


class _Runs:
    def __init__(self, run: RecordedRun) -> None:
        self.run = run

    def load(self, run_id: UUID) -> RecordedRun | None:
        return self.run if run_id == self.run.run_id else None


def test_a_reproduction_that_fails_is_not_a_new_trial(tmp_path: Path) -> None:
    # The recorded run succeeded; its re-execution now fails. The failed run
    # must name what it reproduced, so the store gives it no trial index.
    _write(tmp_path, [_Crashes()])
    manifest = LocalDatasetProvider(tmp_path).open(DATASET).manifest
    recorded = RecordedRun(
        run_id=uuid4(),
        experiment="evaluation",
        strategy="factor_evaluation",
        status="succeeded",
        dataset_id=DATASET,
        dataset_version=1,
        content_hash=manifest.content_hash,
        factor_version="crashes@1",
        parameters={
            key: value for key, value in _payload(feature="crashes").items() if key != "experiment"
        },
        metrics={},
        commit=None,
    )

    with pytest.raises(TrialFailedError) as failure:
        reproduce_run_handler(tmp_path, _Runs(recorded), ENGINES)({"run_id": str(recorded.run_id)})

    assert failure.value.run.status == "failed"
    assert failure.value.run.reproduces_run_id == recorded.run_id


class _TwoOutliers:
    """Ranks by the label, but the top two names sit far out, so wz-1 clips both to one limit."""

    name = "two_outliers"
    version = "1"
    lookback = TRAILING.lookback

    def compute(self, history: History) -> Mapping[str, float]:
        values = dict(TRAILING.compute(history))
        for name in sorted(values, key=values.__getitem__)[-2:]:
            values[name] *= 1_000
        return values


@pytest.mark.parametrize("transform", ["raw", "wz-1"])
def test_decay_at_the_rebalance_horizon_equals_rank_ic_under_each_transform(
    tmp_path: Path, transform: str
) -> None:
    # Under wz-1 the two outliers tie at the clip limit, so the transformed
    # rank IC differs from the raw one; decay must read the same transformed
    # cross-section, and so equal it.
    _write(tmp_path, [_TwoOutliers()])
    handler = factor_evaluation_handler(tmp_path, [*ENGINES, _TwoOutliers()])

    outcome = handler(_payload(feature="two_outliers", transform_version=transform))
    raw = handler(_payload(feature="two_outliers", transform_version="raw")).run

    assert outcome.run is not None
    assert raw is not None
    metrics = outcome.run.metrics
    assert metrics[f"decay_rank_ic_mean_h{HORIZON}"] == metrics["rank_ic_mean"]

    if transform == "wz-1":
        assert metrics["rank_ic_mean"] != raw.metrics["rank_ic_mean"]


# Prices that rise and fall, so returns over different spans rank the names
# differently; a decay read one session short gives a different number.
ZIGZAG = {
    "aaa": [100, 104, 98, 107, 101, 96, 103, 110, 99, 105, 102, 108, 97, 106, 100],
    "bbb": [100, 97, 103, 99, 108, 102, 95, 101, 107, 98, 104, 96, 109, 103, 99],
    "ccc": [100, 102, 101, 99, 103, 106, 104, 98, 102, 109, 100, 105, 103, 97, 108],
    "ddd": [100, 99, 105, 103, 97, 101, 108, 104, 96, 103, 107, 99, 102, 110, 98],
    "eee": [100, 106, 100, 104, 99, 107, 101, 97, 105, 100, 98, 106, 104, 99, 103],
    "fff": [100, 101, 96, 102, 105, 98, 106, 103, 101, 97, 109, 102, 98, 104, 107],
}


def _write_zigzag(tmp_path: Path) -> None:
    write_dataset(
        tmp_path,
        SyntheticDataset(
            dataset_id=DATASET,
            bars=[
                SyntheticBar(name, SESSIONS[index], Decimal(close))
                for name, closes in ZIGZAG.items()
                for index, close in enumerate(closes)
            ],
            spells={name: [(SESSIONS[0], None)] for name in ZIGZAG},
            request={
                "universe": "TEST30",
                "as_of": None,
                "from": SESSIONS[TRAILING.lookback.sessions - 1].isoformat(),
                "to": SESSIONS[-1].isoformat(),
                "interval": "1d",
                "fundamentals": False,
                "rebalance_every": HORIZON,
                "features": [
                    {"name": TRAILING.name, "version": 1, "lookback": TRAILING.lookback.sessions}
                ],
                "label": {"horizon": HORIZON, "basis": "adjusted"},
            },
        ),
    )


def _spearman(left: list[float], right: list[float]) -> float:
    # Tied values share the mean of their ranks (bbb and ccc tie at t = 3).
    def ranks(values: list[float]) -> list[float]:
        return [
            sum(other < value for other in values)
            + (sum(other == value for other in values) - 1) / 2
            for value in values
        ]

    a, b = ranks(left), ranks(right)
    mean_a, mean_b = sum(a) / len(a), sum(b) / len(b)
    covariance = sum((x - mean_a) * (y - mean_b) for x, y in zip(a, b, strict=True))
    spread_a = math.sqrt(sum((x - mean_a) ** 2 for x in a))
    spread_b = math.sqrt(sum((y - mean_b) ** 2 for y in b))
    return covariance / (spread_a * spread_b)


def test_decay_at_twice_the_rebalance_is_the_rank_ic_of_the_return_h_sessions_ahead(
    tmp_path: Path,
) -> None:
    # Rebalances at 3, 6, 9 (12 + 6 passes the end); 9 + 6 = 15 is past it too,
    # so h = 6 keeps 3 and 6. Each IC by hand: trailing 3-session return
    # against the close-to-close return from t to t + 6.
    _write_zigzag(tmp_path)
    names = sorted(ZIGZAG)
    expected = []

    for t in (3, 6):
        trailing = [ZIGZAG[name][t] / ZIGZAG[name][t - 3] - 1 for name in names]
        ahead = [ZIGZAG[name][t + 6] / ZIGZAG[name][t] - 1 for name in names]
        expected.append(_spearman(trailing, ahead))

    outcome = factor_evaluation_handler(tmp_path, ENGINES)(_payload(decay_horizons=[2 * HORIZON]))

    assert outcome.run is not None
    assert outcome.run.metrics[f"decay_periods_h{2 * HORIZON}"] == 2
    assert outcome.run.metrics[f"decay_rank_ic_mean_h{2 * HORIZON}"] == pytest.approx(
        sum(expected) / 2, abs=1e-12
    )


class _ThinAtSix:
    """The constant ranking, but only two names at the second rebalance."""

    name = "thin_at_six"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        names = sorted(history.members())
        kept = names[:2] if history.as_of == SESSIONS[6] else names
        return {name: float(index) for index, name in enumerate(kept)}


def test_turnover_after_a_period_without_a_top_quantile_is_absent(tmp_path: Path) -> None:
    # Rebalances at 3, 6, 9. At 6 there are 2 names for 3 quantiles: no top.
    # Turnover at 9 has no previous top to compare with, so only 0 periods count.
    _write(tmp_path, [_ThinAtSix()])

    outcome = factor_evaluation_handler(tmp_path, [*ENGINES, _ThinAtSix()])(
        _payload(feature="thin_at_six")
    )

    assert outcome.run is not None
    assert outcome.run.metrics["spread_periods"] == 2
    assert outcome.run.metrics["turnover_periods"] == 0
    assert outcome.run.metrics["turnover_mean"] is None
