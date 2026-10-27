"""Price volatility: the dispersion of daily log returns over a trailing window.

Read from the dataset's adjusted closes, so a bonus issue is not scored as a
crash. The deviation is the sample one (``n - 1``), over the window's 60
returns, which need 61 closes.
"""

from __future__ import annotations

import itertools
import math
import statistics
from collections.abc import Mapping

from personal_quant.research.protocols import History, Lookback

__all__ = ["Volatility"]

_WINDOW = 60


class Volatility:
    """The standard deviation of daily log returns of every member with a full window."""

    name = "volatility_60"
    version = "1"
    lookback = Lookback(_WINDOW + 1)

    def compute(self, history: History) -> Mapping[str, float]:
        """Return each member's volatility over the window.

        A member with any close missing in the window, or one at or below
        zero, has no value: a gap is not bridged and a log return of a
        non-positive price does not exist.

        Args:
            history: The harness's view at the as-of.

        Returns:
            The volatility per member that has one.
        """
        values: dict[str, float] = {}

        for instrument in sorted(history.members()):
            closes = history.closes(instrument, self.lookback.sessions)

            if any(close is None or close <= 0 for close in closes):
                continue

            prices = [float(close) for close in closes if close is not None]
            returns = [math.log(end / start) for start, end in itertools.pairwise(prices)]
            values[instrument] = statistics.stdev(returns)

        return values
