"""Probability calibration and expected value, net of a declared cost (ADR-037).

The model probability is the running frequency of ``forward return > 0`` over
labels already known at the rebalance: :func:`known_at` hands the estimator
only the rebalances whose **exit** is at or before *t*, which is the
structural guard of ADR-037 §4, since labels never pass through the harness.

:func:`calibrate` scores probabilities against realised outcomes in three bins
at the predictions' own terciles; :func:`expected_value` prices a probability
net of a :class:`CostModel`. Anything with an absent input is absent, never 0.
"""

from __future__ import annotations

import math
import statistics
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import date
from typing import Protocol

__all__ = [
    "Calibration",
    "CalibrationBin",
    "CostModel",
    "calibrate",
    "expected_value",
    "known_at",
    "p_model",
]

MINIMUM_OBSERVATIONS = 20
"""Below this many past labels the running frequency is absent (ADR-037 §1)."""

_BINS = 3


class _Exits(Protocol):
    @property
    def exit(self) -> date: ...


@dataclass(frozen=True, slots=True)
class CalibrationBin:
    """One bin: its mean prediction, realised frequency and count; ``None`` when empty."""

    predicted: float | None
    realised: float | None
    count: int


@dataclass(frozen=True, slots=True)
class Calibration:
    """Brier score, count-weighted reliability gap, observation count and the three bins."""

    brier: float | None
    gap: float | None
    count: int
    bins: tuple[CalibrationBin, ...]


@dataclass(frozen=True, slots=True)
class CostModel:
    """A round trip's costs as fractions of notional; a declared parameter, never a constant."""

    buy_fee: float | None
    sell_fee: float | None
    sell_tax: float | None
    spread: float | None
    slippage: float | None

    @property
    def round_trip(self) -> float | None:
        """Buy and sell fees, the sell tax, the spread once and slippage on both legs."""
        figures = (self.buy_fee, self.sell_fee, self.sell_tax, self.spread, self.slippage)

        if any(figure is None for figure in figures):
            return None

        return (
            self.buy_fee + self.sell_fee + self.sell_tax + self.spread + 2 * self.slippage  # type: ignore[operator]
        )


def known_at[R: _Exits](rebalances: Sequence[R], t: date) -> list[R]:
    """Return the rebalances whose label is known at ``t``: exit at or before it.

    A rebalance whose session is before ``t`` but whose exit is after it has a
    label nobody had at ``t``; reading the session instead would leak it.
    """
    return [rebalance for rebalance in rebalances if rebalance.exit <= t]


def p_model(labels: Sequence[float], minimum: int = MINIMUM_OBSERVATIONS) -> float | None:
    """Return the frequency of ``label > 0``, or ``None`` below ``minimum`` labels."""
    if len(labels) < minimum:
        return None

    return sum(label > 0 for label in labels) / len(labels)


def calibrate(predictions: Sequence[float], outcomes: Sequence[int]) -> Calibration:
    """Score probabilities against outcomes of 0 or 1.

    The bins are cut at the predictions' own terciles, at an observed value, so
    equal predictions always share a bin: a constant model fills one bin and
    leaves two empty, with a gap of 0. The gap is the count-weighted mean of
    ``|predicted - realised|`` over the bins that hold anything.

    Raises:
        ValueError: The two sequences differ in length.
    """
    if len(predictions) != len(outcomes):
        raise ValueError(
            f"{len(predictions)} predictions cannot be scored against {len(outcomes)} outcomes."
        )

    count = len(predictions)

    if count == 0:
        empty = CalibrationBin(None, None, 0)
        return Calibration(None, None, 0, (empty,) * _BINS)

    brier = statistics.fmean((p - o) ** 2 for p, o in zip(predictions, outcomes, strict=True))

    # Order statistics, not interpolated quantiles: an edge is a prediction
    # that occurs, so a value equal to it lands in the same bin as the edge.
    ordered = sorted(predictions)
    edges = [ordered[math.ceil(count * k / _BINS) - 1] for k in range(1, _BINS)]
    members: list[list[tuple[float, int]]] = [[] for _ in range(_BINS)]

    for p, o in zip(predictions, outcomes, strict=True):
        index = next((k for k, edge in enumerate(edges) if p <= edge), _BINS - 1)
        members[index].append((p, o))

    bins = tuple(
        CalibrationBin(
            statistics.fmean(p for p, _ in held) if held else None,
            statistics.fmean(o for _, o in held) if held else None,
            len(held),
        )
        for held in members
    )
    gap = (
        sum(
            item.count * abs(item.predicted - item.realised)  # type: ignore[operator]
            for item in bins
            if item.count
        )
        / count
    )

    return Calibration(brier, gap, count, bins)


def expected_value(
    p: float | None, gain: float | None, loss: float | None, cost: CostModel | None
) -> float | None:
    """Return ``p * gain - (1 - p) * loss - round trip``; ``None`` when any input is.

    ``loss`` is a magnitude (the mean of ``-label`` over past losers), so it is
    positive and the second term subtracts it (ADR-037 §3).
    """
    round_trip = None if cost is None else cost.round_trip

    if p is None or gain is None or loss is None or round_trip is None:
        return None

    return p * gain - (1 - p) * loss - round_trip
