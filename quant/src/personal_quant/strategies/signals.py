"""Turning factor scores into a signal."""

from __future__ import annotations

from collections.abc import Mapping

__all__ = ["CrossSectionalRank"]


class CrossSectionalRank:
    """Ranks one cross-section into percentiles in ``(0, 1]``.

    A rank rather than the raw score, so that one extreme value — a stock
    that tripled, a data error that survived — moves its own position and no
    one else's. Tied scores share the average of the ranks they span.
    """

    def signal(self, scores: Mapping[str, float]) -> Mapping[str, float]:
        """Rank the scores.

        Args:
            scores: One factor value per instrument.

        Returns:
            The percentile rank per instrument; the highest score is ``1.0``.
        """
        ordered = sorted(scores.items(), key=lambda item: (item[1], item[0]))
        count = len(ordered)
        ranks: dict[str, float] = {}
        position = 0

        while position < count:
            end = position

            while end + 1 < count and ordered[end + 1][1] == ordered[position][1]:
                end += 1

            # Ranks are 1-based; the tie spans position + 1 to end + 1.
            shared = (position + end + 2) / 2

            for index in range(position, end + 1):
                ranks[ordered[index][0]] = shared / count

            position = end + 1

        return ranks
