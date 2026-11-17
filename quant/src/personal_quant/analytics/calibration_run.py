"""The calibration job's computation: p_model, calibration and expected value per build (ADR-037).

For each rebalance *t* and each name in a quantile of the transformed feature,
``p_model`` is the frequency of a positive label in that quantile over the
rebalances already known at *t* (:func:`~personal_quant.analytics.calibration.known_at`:
exit at or before *t*). ``gain`` and ``loss`` come from the same past labels.
The realised outcome at *t* is the rebalance's own label, read outside the
harness. Names without a ``p_model`` (fewer than 20 past labels) are not
scored, and their expected value is absent, never 0.
"""

from __future__ import annotations

import statistics
from collections.abc import Callable, Mapping

from personal_quant.analytics.calibration import (
    CostModel,
    calibrate,
    expected_value,
    known_at,
    p_model,
)
from personal_quant.research.labelled import LabelledDataset, Rebalance
from personal_quant.strategies.portfolio import quantile_buckets

__all__ = ["calibration_metrics"]

Transform = Callable[[Mapping[str, float]], Mapping[str, float]]


def calibration_metrics(
    labelled: LabelledDataset,
    feature: str,
    transform: Transform,
    quantiles: int,
    cost: CostModel,
) -> dict[str, float | None]:
    """Score one feature's running probabilities and price them net of ``cost``.

    Args:
        labelled: The labelled dataset.
        feature: A feature its request names.
        transform: The cross-sectional transform applied per rebalance.
        quantiles: How many buckets the transformed feature is split into.
        cost: The declared cost model.

    Returns:
        ``observations``, ``brier``, ``gap``, each bin's count, predicted mean
        and realised frequency, ``ev_mean``, ``ev_count`` and
        ``ev_positive_share``, each count beside the number it qualifies.
    """
    rebalances = list(labelled.rebalances(only=feature))
    # Each name's quantile at each rebalance, lowest first, from that
    # rebalance's own transformed cross-section.
    quantile_of: list[dict[str, int]] = [
        _quantiles(item, feature, transform, quantiles) for item in rebalances
    ]

    predictions: list[float] = []
    outcomes: list[int] = []
    values: list[float] = []

    for index, rebalance in enumerate(rebalances):
        past = known_at(rebalances, rebalance.session)

        for name, bucket in quantile_of[index].items():
            if name not in rebalance.label:
                continue

            history = [
                item.label[other]
                for item in past
                for other, other_bucket in quantile_of[rebalances.index(item)].items()
                if other_bucket == bucket and other in item.label
            ]
            probability = p_model(history)

            if probability is None:
                continue

            winners = [label for label in history if label > 0]
            losers = [-label for label in history if label <= 0]
            value = expected_value(
                probability,
                statistics.fmean(winners) if winners else None,
                statistics.fmean(losers) if losers else None,
                cost,
            )

            predictions.append(probability)
            outcomes.append(1 if rebalance.label[name] > 0 else 0)

            if value is not None:
                values.append(value)

    result = calibrate(predictions, outcomes)
    metrics: dict[str, float | None] = {
        "observations": float(result.count),
        "brier": result.brier,
        "gap": result.gap,
        "ev_mean": statistics.fmean(values) if values else None,
        "ev_count": float(len(values)),
        "ev_positive_share": (sum(value > 0 for value in values) / len(values)) if values else None,
        "rebalances": float(len(rebalances)),
    }

    for position, item in enumerate(result.bins, start=1):
        metrics[f"bin_{position}_count"] = float(item.count)
        metrics[f"bin_{position}_predicted"] = item.predicted
        metrics[f"bin_{position}_realised"] = item.realised

    return metrics


def _quantiles(
    rebalance: Rebalance, feature: str, transform: Transform, quantiles: int
) -> dict[str, int]:
    scores = transform(rebalance.features[feature])

    # Fewer names than quantiles has no buckets; that rebalance scores nothing.
    if len(scores) < quantiles:
        return {}

    return {
        name: position
        for position, bucket in enumerate(quantile_buckets(dict(scores), quantiles))
        for name in bucket
    }
