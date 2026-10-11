"""Close-to-close holding-period returns.

**Not a backtest.** No transaction costs, no price-band or lot-size limits,
no T+2 settlement: a name is bought at one session's close and valued at a
later one's. That is enough to measure whether a signal ranks future returns,
which is what the U6 vertical slice has to show, and nothing more. The
event-driven backtester under Vietnamese market rules is Phase 8's, behind
the :class:`~personal_quant.research.protocols.Backtester` protocol.

These returns are labels: they read sessions after the rebalance on purpose,
so they are computed here and never through the point-in-time harness that
engines are confined to.
"""

from __future__ import annotations

from collections.abc import Iterable, Mapping
from datetime import date

from personal_quant.research.protocols import Dataset

__all__ = ["forward_returns", "weighted_return"]


def forward_returns(
    dataset: Dataset, start: date, end: date, instruments: Iterable[str]
) -> dict[str, float]:
    """Return each instrument's return from one session's close to a later one's.

    An instrument that leaves the universe inside the period is valued at its
    last close while still a member — the entry close itself when it left the
    very next session — rather than dropped, which would quietly remove the
    leavers from every portfolio holding them. Its bars after leaving are not
    used even when the dataset has them: a build exported with history holds a
    re-admitted name's bars between its spells, and those are data a feature
    may read, not a position anyone held. A name that left for good and one
    that came back are valued the same way.

    Entry is at the close of the session the signal was computed at, which a
    factor skipping no sessions has just read; a factor that needs a gap
    between signal and trade declares it in its own window.

    Args:
        dataset: The verified dataset.
        start: The entry session.
        end: The exit session.
        instruments: The instruments to value.

    Returns:
        The return per instrument with a close at ``start`` and a later one.
    """
    returns: dict[str, float] = {}

    for instrument in sorted(instruments):
        bars = dataset.bars_of(instrument)
        entry = bars.get(start)

        if entry is None or entry.close <= 0:
            continue

        last = start

        for session in sorted(day for day in bars if start < day <= end):
            if instrument not in dataset.members_on(session):
                break

            last = session

        returns[instrument] = float(bars[last].close / entry.close) - 1.0

    return returns


def weighted_return(weights: Mapping[str, float], returns: Mapping[str, float]) -> float | None:
    """Return a portfolio's return over the holdings that have one.

    Weights are rescaled over the holdings with a return, so a holding with
    no valuation neither counts as zero nor inflates the others' share.

    Args:
        weights: Target weight per instrument.
        returns: Return per instrument.

    Returns:
        The weighted return, or ``None`` when no holding has one.
    """
    priced = {instrument: weight for instrument, weight in weights.items() if instrument in returns}
    total = sum(priced.values())

    if total <= 0:
        return None

    return sum(weight * returns[instrument] for instrument, weight in priced.items()) / total
