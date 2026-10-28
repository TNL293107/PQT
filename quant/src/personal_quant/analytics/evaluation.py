"""Factor evaluation over a labelled dataset: one trial's metrics (ADR-036).

Features come only from :class:`~personal_quant.research.labelled.LabelledDataset`,
through the harness; labels and decay returns only from
:func:`~personal_quant.backtesting.returns.forward_returns`, outside it. Each
cross-section is transformed on its own (ADR-035) before anything reads it.

Every series is summarised with its period count. A period that cannot carry a
metric (fewer than three names, no spread, fewer names than quantiles) has
none, and the count says so; nothing is filled with zero.
"""

from __future__ import annotations

import itertools
import statistics
from collections.abc import Callable, Mapping, Sequence

from personal_quant.analytics.information import (
    InformationSummary,
    information_coefficient,
    pearson_information_coefficient,
    summarise,
)
from personal_quant.backtesting.returns import forward_returns
from personal_quant.research.labelled import LabelledDataset
from personal_quant.strategies.portfolio import quantile_buckets

__all__ = ["evaluate_factor", "factor_correlation"]

Transform = Callable[[Mapping[str, float]], Mapping[str, float]]


def evaluate_factor(
    labelled: LabelledDataset,
    feature: str,
    transform: Transform,
    quantiles: int,
    decay_horizons: Sequence[int],
) -> dict[str, float | None]:
    """Evaluate one feature under one transform: rank IC, IC, spread, turnover, decay.

    Args:
        labelled: The labelled dataset.
        feature: A feature its request names.
        transform: The cross-sectional transform.
        quantiles: Buckets for the spread and the top quantile.
        decay_horizons: Sessions ahead at which the mean rank IC is measured.

    Returns:
        The metrics by name, each series with its period count.
    """
    dataset = labelled.dataset
    sessions = list(dataset.sessions)
    rank_ic: list[float] = []
    ic: list[float] = []
    spread: list[float] = []
    turnover: list[float] = []
    coverage: list[float] = []
    decay: dict[int, list[float]] = {horizon: [] for horizon in decay_horizons}
    previous_top: frozenset[str] | None = None
    periods = 0

    for rebalance in labelled.rebalances():
        periods += 1
        scores = transform(rebalance.features[feature])
        both = {name: scores[name] for name in scores if name in rebalance.label}
        coverage.append(float(len(both)))

        _append(rank_ic, information_coefficient(scores, rebalance.label))
        _append(ic, pearson_information_coefficient(scores, rebalance.label))

        top: frozenset[str] | None = None

        if quantiles <= len(both):
            buckets = quantile_buckets(both, quantiles)
            top = buckets[-1]
            spread.append(_mean_of(rebalance.label, top) - _mean_of(rebalance.label, buckets[0]))

            if previous_top is not None:
                turnover.append(len(top - previous_top) / len(top))

        previous_top = top
        index = sessions.index(rebalance.session)
        members = dataset.members_on(rebalance.session)

        for horizon in decay_horizons:
            # Dropped for this horizon when t + h passes the last session (ADR-036 §3).
            if index + horizon < len(sessions):
                ahead = forward_returns(
                    dataset, rebalance.session, sessions[index + horizon], members
                )
                _append(decay[horizon], information_coefficient(scores, ahead))

    metrics: dict[str, float | None] = {
        "periods": float(periods),
        "coverage_mean": statistics.fmean(coverage) if coverage else None,
    }
    metrics |= _summary("rank_ic", summarise(rank_ic))
    metrics |= _summary("ic", summarise(ic))
    metrics |= _summary("spread", summarise(spread))
    metrics["turnover_periods"] = float(len(turnover))
    metrics["turnover_mean"] = statistics.fmean(turnover) if turnover else None

    # Overlapping horizons have autocorrelated IC: the mean and the count only.
    for horizon, series in decay.items():
        metrics[f"decay_rank_ic_mean_h{horizon}"] = summarise(series).mean
        metrics[f"decay_periods_h{horizon}"] = float(len(series))

    return metrics


def factor_correlation(labelled: LabelledDataset, transform: Transform) -> dict[str, float | None]:
    """Average, over rebalances, the rank correlation of every pair of features.

    Per rebalance rather than pooled, which would mix the cross-section with
    time (ADR-036 §4).

    Args:
        labelled: The labelled dataset.
        transform: The cross-sectional transform applied to each feature first.

    Returns:
        ``corr_<a>__<b>_mean`` and ``corr_<a>__<b>_periods`` for each pair, by name.
    """
    names = sorted(feature.name for feature in labelled.request.features)
    pairs = list(itertools.combinations(names, 2))
    series: dict[tuple[str, str], list[float]] = {pair: [] for pair in pairs}

    for rebalance in labelled.rebalances():
        transformed = {name: transform(rebalance.features[name]) for name in names}

        for left, right in pairs:
            _append(
                series[left, right], information_coefficient(transformed[left], transformed[right])
            )

    metrics: dict[str, float | None] = {}

    for (left, right), values in series.items():
        metrics[f"corr_{left}__{right}_mean"] = statistics.fmean(values) if values else None
        metrics[f"corr_{left}__{right}_periods"] = float(len(values))

    return metrics


def _append(series: list[float], value: float | None) -> None:
    if value is not None:
        series.append(value)


def _mean_of(values: Mapping[str, float], names: frozenset[str]) -> float:
    return statistics.fmean(values[name] for name in names)


def _summary(prefix: str, summary: InformationSummary) -> dict[str, float | None]:
    return {
        f"{prefix}_periods": float(summary.periods),
        f"{prefix}_mean": summary.mean,
        f"{prefix}_deviation": summary.deviation,
        f"{prefix}_information_ratio": summary.information_ratio,
        f"{prefix}_t_statistic": summary.t_statistic,
    }
