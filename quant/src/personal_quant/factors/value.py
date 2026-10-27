"""Value: earnings and book value per share over the raw close, E/P and B/P (ADR-033).

Inverses, not multiples: a loss or negative equity is a negative number that
still orders, where P/E and P/B would be absent or explode near zero. Both
inputs come from the fundamentals row at the as-of, so the per-share values
are the ratio service's and the close is on the same raw basis.
"""

from __future__ import annotations

from collections.abc import Mapping

from personal_quant.research.protocols import History, Lookback

__all__ = ["BookToPrice", "EarningsToPrice"]


class _PerShareOverClose:
    """A per-share value of the row divided by its raw close."""

    name: str
    numerator: str
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        """Return each member's per-share value over its raw close.

        A member without a fundamentals row, with the value or the close
        absent, or with a close at or below zero, has no value.

        Args:
            history: The harness's view at the as-of.

        Returns:
            The ratio per member that has one.
        """
        values: dict[str, float] = {}

        for instrument in sorted(history.members()):
            row = history.fundamentals(instrument)

            if row is None:
                continue

            value, close = row.values[self.numerator], row.values["close_raw"]

            if value is None or close is None or close <= 0:
                continue

            values[instrument] = float(value / close)

        return values


class EarningsToPrice(_PerShareOverClose):
    """EPS (TTM) over the raw close; a loss is negative, not absent."""

    name = "value_ep"
    numerator = "eps_ttm"


class BookToPrice(_PerShareOverClose):
    """Book value per share over the raw close; negative equity is negative, not absent."""

    name = "value_bp"
    numerator = "book_value_per_share"
