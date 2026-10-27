"""Cross-sectional transforms: one rebalance's cross-section in, the same names out (ADR-035).

A transform reads one feature at one rebalance and nothing else: not other
sessions, not other features, not the label. Fitting over the pooled panel
would leak the dispersion of later sessions into earlier ones.

Each transform carries a version, recorded with every run (ADR-036): ``raw``
is the identity; ``wz-1`` winsorises at median ± 3 scaled MAD and z-scores
with the sample deviation, on at least 5 names. Changing a rule, a constant
or the order is a new version.
"""

from __future__ import annotations

import statistics
from collections.abc import Callable, Mapping

__all__ = ["TRANSFORMS", "identity", "winsorise_zscore"]

MIN_NAMES = 5
"""The smallest cross-section a median and a deviation carry anything on."""

CLIP_MADS = 3.0
MAD_SCALE = 1.4826
"""Scales the median absolute deviation to a normal's standard deviation."""


def identity(cross_section: Mapping[str, float]) -> dict[str, float]:
    """Return the cross-section as it is: version ``raw``."""
    return dict(cross_section)


def winsorise_zscore(cross_section: Mapping[str, float]) -> dict[str, float]:
    """Winsorise at median ± 3 scaled MAD, then z-score: version ``wz-1``.

    Args:
        cross_section: One feature's values at one rebalance; an absent name
            is not a key.

    Returns:
        The same names transformed, or empty with fewer than 5 names or a
        winsorised deviation of zero. No name is added, and none is given 0
        or the mean.
    """
    if len(cross_section) < MIN_NAMES:
        return {}

    values = list(cross_section.values())
    median = statistics.median(values)
    mad = statistics.median(abs(value - median) for value in values) * MAD_SCALE

    # A MAD of 0 (most names equal) leaves the values unclipped (ADR-035 §1.2).
    if mad > 0:
        low, high = median - CLIP_MADS * mad, median + CLIP_MADS * mad
        clipped = {name: min(max(value, low), high) for name, value in cross_section.items()}
    else:
        clipped = dict(cross_section)

    mean = statistics.fmean(clipped.values())
    deviation = statistics.stdev(clipped.values())

    if deviation == 0:
        return {}

    return {name: (value - mean) / deviation for name, value in clipped.items()}


TRANSFORMS: Mapping[str, Callable[[Mapping[str, float]], dict[str, float]]] = {
    "raw": identity,
    "wz-1": winsorise_zscore,
}
"""Each transform by the version a run records."""
