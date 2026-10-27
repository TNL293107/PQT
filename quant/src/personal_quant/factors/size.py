"""Size: market capitalisation, the share count times the raw close.

Both come from the dataset's fundamentals row at the as-of (ADR-033), so the
share count is the one the ratio service used and the close is on the same,
unadjusted basis. ADR-030's treasury-share bias overstates it for an issuer
holding treasury shares. For BCM, MSN, MWG, PLX, POW, VNM, VPL and VRE the
share count rests on 24 corporate actions transcribed by hand from VSDC
notices (ADR-032).
"""

from __future__ import annotations

from collections.abc import Mapping

from personal_quant.research.protocols import History, Lookback

__all__ = ["Size"]


class Size:
    """The market capitalisation of every member whose row states both inputs."""

    name = "size"
    version = "1"
    lookback = Lookback(1)

    def compute(self, history: History) -> Mapping[str, float]:
        """Return each member's market capitalisation in VND.

        A member without a fundamentals row, whose shares or raw close is
        absent, or whose raw close is at or below zero, has no value.

        Args:
            history: The harness's view at the as-of.

        Returns:
            The capitalisation per member that has one.
        """
        values: dict[str, float] = {}

        for instrument in sorted(history.members()):
            row = history.fundamentals(instrument)

            if row is None:
                continue

            shares, close = row.values["shares"], row.values["close_raw"]

            if shares is None or close is None or close <= 0:
                continue

            values[instrument] = float(shares * close)

        return values
