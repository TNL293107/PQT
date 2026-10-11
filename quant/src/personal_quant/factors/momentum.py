"""Price momentum: the return over a formation window, skipping the latest sessions.

The classic 12-1 factor is the default: the return from 252 sessions ago to
21 sessions ago. The most recent month is left out because over that horizon
returns tend to reverse rather than continue, and mixing the two dilutes the
effect being measured.

Read from the dataset's adjusted closes, which already account for corporate
actions; a raw series would score every bonus issue as a crash.
"""

from __future__ import annotations

from collections.abc import Mapping

from personal_quant.research.protocols import History, Lookback

__all__ = ["Momentum"]

_TRADING_YEAR = 252
_TRADING_MONTH = 21


class Momentum:
    """The formation-window return of every member that has one."""

    def __init__(self, formation: int = _TRADING_YEAR, skip: int = _TRADING_MONTH) -> None:
        """Choose the window.

        Args:
            formation: Sessions from the window's start to the as-of.
            skip: The most recent sessions left out of the window.

        Raises:
            ValueError: The window measures no return.
        """
        if formation < 1 or not 0 <= skip < formation:
            raise ValueError(
                f"A formation of {formation} sessions skipping {skip} measures no return."
            )

        self._formation = formation
        self._skip = skip

    @property
    def name(self) -> str:
        """``momentum_12_1`` for the default window, otherwise the sessions."""
        if (self._formation, self._skip) == (_TRADING_YEAR, _TRADING_MONTH):
            return "momentum_12_1"

        return f"momentum_{self._formation}_{self._skip}"

    @property
    def version(self) -> str:
        """Changes whenever the computation does."""
        return "1"

    @property
    def lookback(self) -> Lookback:
        """The formation window and the as-of session itself."""
        return Lookback(self._formation + 1)

    def compute(self, history: History) -> Mapping[str, float]:
        """Return each member's formation-window return.

        A member without a close at either end of the window — too recently
        listed or admitted, or with no bar that session — has no value rather
        than one taken from a neighbouring session.

        Args:
            history: The harness's view at the as-of.

        Returns:
            The return per member that has one.
        """
        values: dict[str, float] = {}

        for instrument in sorted(history.members()):
            closes = history.closes(instrument, self.lookback.sessions)
            start, end = closes[0], closes[-1 - self._skip]

            if start is None or end is None or start <= 0:
                continue

            values[instrument] = float(end / start) - 1.0

        return values
