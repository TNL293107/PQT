"""The calibration job's computation (ADR-037 §1, §3, §4), against values recomputed by hand."""

from __future__ import annotations

import statistics
from datetime import date
from pathlib import Path

import pytest

from personal_quant.analytics.calibration import CostModel, calibrate, expected_value
from personal_quant.analytics.calibration_run import calibration_metrics
from personal_quant.factors.transforms import identity
from personal_quant.research.datasets import LocalDatasetProvider
from personal_quant.research.labelled import LabelledDataset
from personal_quant.strategies.portfolio import quantile_buckets
from test_calibration_handler_acceptance import DATASET, ENGINES, TRAILING, _write

COST = CostModel(buy_fee=0.0015, sell_fee=0.0015, sell_tax=0.001, spread=0.001, slippage=0.0)


def _by_hand(
    labelled: LabelledDataset, quantiles: int
) -> tuple[list[float], list[int], list[float]]:
    """Recompute every scored prediction from the rule in ADR-037, independently of the code."""
    rebalances = list(labelled.rebalances(only=TRAILING.name))
    buckets: list[dict[str, int]] = []

    for item in rebalances:
        scores = item.features[TRAILING.name]
        buckets.append(
            {}
            if len(scores) < quantiles
            else {
                name: k
                for k, bucket in enumerate(quantile_buckets(dict(scores), quantiles))
                for name in bucket
            }
        )

    predictions: list[float] = []
    outcomes: list[int] = []
    values: list[float] = []

    for index, item in enumerate(rebalances):
        known: list[tuple[int, date]] = [
            (j, other.exit) for j, other in enumerate(rebalances) if other.exit <= item.session
        ]

        for name, bucket in buckets[index].items():
            if name not in item.label:
                continue

            history = [
                rebalances[j].label[other]
                for j, _ in known
                for other, other_bucket in buckets[j].items()
                if other_bucket == bucket and other in rebalances[j].label
            ]

            if len(history) < 20:
                continue

            p = sum(label > 0 for label in history) / len(history)
            winners = [label for label in history if label > 0]
            losers = [-label for label in history if label <= 0]
            predictions.append(p)
            outcomes.append(1 if item.label[name] > 0 else 0)
            value = expected_value(
                p,
                statistics.fmean(winners) if winners else None,
                statistics.fmean(losers) if losers else None,
                COST,
            )

            if value is not None:
                values.append(value)

    return predictions, outcomes, values


@pytest.fixture
def labelled(tmp_path: Path) -> LabelledDataset:
    _write(tmp_path)
    return LabelledDataset.open(LocalDatasetProvider(tmp_path), DATASET, ENGINES)


def test_the_metrics_equal_the_rule_recomputed_by_hand(labelled: LabelledDataset) -> None:
    predictions, outcomes, values = _by_hand(labelled, 3)
    expected = calibrate(predictions, outcomes)

    metrics = calibration_metrics(labelled, TRAILING.name, identity, 3, COST)

    assert predictions, "the build must score something, or the test proves nothing"
    assert metrics["observations"] == len(predictions)
    assert metrics["brier"] == pytest.approx(expected.brier, abs=1e-12)
    assert metrics["gap"] == pytest.approx(expected.gap, abs=1e-12)
    assert metrics["ev_count"] == len(values)
    assert metrics["ev_mean"] == pytest.approx(statistics.fmean(values), abs=1e-12)
    for index, item in enumerate(expected.bins, start=1):
        assert metrics[f"bin_{index}_count"] == item.count


def test_a_rebalance_s_own_label_never_enters_its_probability(labelled: LabelledDataset) -> None:
    # The first rebalances have no known labels at all, so nothing is scored
    # there: a prediction exists only once 20 labels with exit <= t exist.
    rebalances = list(labelled.rebalances(only=TRAILING.name))
    first_scorable = next(
        index
        for index, item in enumerate(rebalances)
        if sum(1 for other in rebalances if other.exit <= item.session) * 2 >= 20
    )

    predictions, _, _ = _by_hand(labelled, 3)
    metrics = calibration_metrics(labelled, TRAILING.name, identity, 3, COST)

    assert first_scorable > 0
    assert metrics["observations"] == len(predictions)
    assert metrics["observations"] < sum(len(item.label) for item in rebalances)


def test_with_too_few_names_for_the_quantiles_nothing_is_scored(labelled: LabelledDataset) -> None:
    metrics = calibration_metrics(labelled, TRAILING.name, identity, 7, COST)

    assert metrics["observations"] == 0
    assert metrics["brier"] is None
    assert metrics["ev_mean"] is None
    assert metrics["ev_count"] == 0
