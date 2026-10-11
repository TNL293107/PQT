"""Information coefficient: how well a factor ranks the returns that followed it."""

from __future__ import annotations

import math
import statistics
from collections.abc import Mapping, Sequence
from dataclasses import dataclass

__all__ = ["InformationSummary", "information_coefficient", "summarise"]

_MINIMUM_NAMES = 3
_MINIMUM_PERIODS = 2


@dataclass(frozen=True, slots=True)
class InformationSummary:
    """The IC series reduced to its mean and its consistency.

    ``information_ratio`` is the mean IC over its standard deviation: a factor
    with a small IC every period is worth more than one with a large IC some
    periods and a negative one the rest. ``t_statistic`` scales it by the
    square root of the period count. Neither accounts for how many factors
    were tried before this one; that needs the experiment log (U9).
    """

    periods: int
    mean: float | None
    deviation: float | None
    information_ratio: float | None
    t_statistic: float | None


def information_coefficient(
    scores: Mapping[str, float], returns: Mapping[str, float]
) -> float | None:
    """Return the Spearman rank correlation of scores with later returns.

    Args:
        scores: The factor value per instrument at the rebalance.
        returns: The return per instrument over the following period.

    Returns:
        The rank correlation over the instruments that have both, or ``None``
        when fewer than three do or either side has no spread.
    """
    common = sorted(set(scores) & set(returns))

    if len(common) < _MINIMUM_NAMES:
        return None

    left = [scores[instrument] for instrument in common]
    right = [returns[instrument] for instrument in common]

    if len(set(left)) == 1 or len(set(right)) == 1:
        return None

    return statistics.correlation(left, right, method="ranked")


def summarise(coefficients: Sequence[float]) -> InformationSummary:
    """Reduce an IC series to its mean, deviation, ratio and t-statistic.

    Args:
        coefficients: One IC per period.

    Returns:
        The summary; the ratio and t-statistic are ``None`` with fewer than
        two periods or no variation, as there is nothing to divide by.
    """
    count = len(coefficients)

    if count == 0:
        return InformationSummary(0, None, None, None, None)

    mean = statistics.fmean(coefficients)

    if count < _MINIMUM_PERIODS:
        return InformationSummary(count, mean, None, None, None)

    deviation = statistics.stdev(coefficients)

    if deviation == 0:
        return InformationSummary(count, mean, deviation, None, None)

    ratio = mean / deviation

    return InformationSummary(count, mean, deviation, ratio, ratio * math.sqrt(count))
