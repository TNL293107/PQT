"""Liquidity: the median value traded per session over a trailing window.

Read from turnover, the VND traded as the source recorded it. Unlike the
dataset's volume, which is adjusted by the share factor, turnover needs no
basis and compares across names (ADR-033).
"""

from __future__ import annotations

import statistics
from collections.abc import Mapping

from personal_quant.research.protocols import History, Lookback

__all__ = ["Liquidity"]

_WINDOW = 20


class Liquidity:
    """The median turnover of every member with a bar and a turnover on every session."""

    name = "liquidity_20"
    version = "1"
    lookback = Lookback(_WINDOW)

    def compute(self, history: History) -> Mapping[str, float]:
        """Return each member's median turnover over the window.

        A member with a session missing in the window, or one whose turnover
        the source did not report, has no value rather than a median of
        fewer sessions.

        Args:
            history: The harness's view at the as-of.

        Returns:
            The median turnover in VND per member that has one.
        """
        values: dict[str, float] = {}

        for instrument in sorted(history.members()):
            turnovers = [
                None if bar is None else bar.turnover
                for bar in history.bars(instrument, self.lookback.sessions)
            ]

            if any(turnover is None for turnover in turnovers):
                continue

            values[instrument] = float(statistics.median(t for t in turnovers if t is not None))

        return values
