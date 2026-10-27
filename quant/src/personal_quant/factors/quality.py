"""Quality: return on equity (TTM) as the dataset states it (ADR-033).

The ratio service computes it once (ADR-030); the feature only reads it at
the as-of, so there is one TTM rule.
"""

from __future__ import annotations

from collections.abc import Mapping

from personal_quant.research.protocols import History, Lookback

__all__ = ["ReturnOnEquity"]


class ReturnOnEquity:
    """The ROE of every member whose row states one."""

    name = "quality_roe"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        """Return each member's ROE.

        Args:
            history: The harness's view at the as-of.

        Returns:
            The ROE per member that has one; a member without a row, or with
            ``roe`` absent, has none.
        """
        values: dict[str, float] = {}

        for instrument in sorted(history.members()):
            row = history.fundamentals(instrument)

            if row is not None and (roe := row.values["roe"]) is not None:
                values[instrument] = float(roe)

        return values
