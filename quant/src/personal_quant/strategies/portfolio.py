"""From a signal to a portfolio: quantile buckets and the top quantile."""

from __future__ import annotations

from collections.abc import Mapping

__all__ = ["TopQuantile", "quantile_buckets"]


def quantile_buckets(signal: Mapping[str, float], quantiles: int) -> list[frozenset[str]]:
    """Split a cross-section into quantiles, lowest signal first.

    Ties are broken by instrument id, so the same signal always produces the
    same buckets. Sizes differ by at most one when the count does not divide.

    Args:
        signal: One signal value per instrument.
        quantiles: How many buckets; ten for deciles.

    Returns:
        The buckets, from the lowest signal to the highest.

    Raises:
        ValueError: There are fewer instruments than buckets, so one would
            be empty and its return undefined.
    """
    count = len(signal)

    if quantiles < 1 or count < quantiles:
        raise ValueError(f"{count} instruments cannot fill {quantiles} quantiles.")

    ordered = sorted(signal, key=lambda instrument: (signal[instrument], instrument))
    buckets: list[set[str]] = [set() for _ in range(quantiles)]

    for position, instrument in enumerate(ordered):
        buckets[position * quantiles // count].add(instrument)

    return [frozenset(bucket) for bucket in buckets]


class TopQuantile:
    """Holds the highest-signal quantile in equal weights."""

    def __init__(self, quantiles: int = 10) -> None:
        """Choose the quantile count.

        Args:
            quantiles: How many buckets the cross-section is split into.
        """
        self._quantiles = quantiles

    def weights(self, signal: Mapping[str, float]) -> Mapping[str, float]:
        """Return equal weights over the top quantile.

        Args:
            signal: One signal value per instrument.

        Returns:
            The weight of each instrument held.
        """
        top = quantile_buckets(signal, self._quantiles)[-1]

        return {instrument: 1.0 / len(top) for instrument in sorted(top)}
