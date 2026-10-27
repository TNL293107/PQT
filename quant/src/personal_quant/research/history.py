"""The point-in-time harness: what an engine is allowed to see.

An engine never reads a dataset. It is handed a :class:`PointInTimeHistory`
cut off at the as-of session and no deeper than the lookback the engine
declared, and :func:`evaluate` checks what comes back. Three ways of using
information a strategy could not have had are caught here rather than
discouraged in a comment:

* reading a session after the as-of — :class:`LookAheadError`;
* reading further back than the declared lookback — :class:`LookbackExceededError`.
  A lookback that is not the real one cannot be checked against a dataset's
  history, and one that silently reads more is how a warm-up period leaks;
* scoring an instrument that was not a member on the as-of —
  :class:`NonMemberScoredError`. That is survivorship: a score for a security
  no strategy could have chosen from the universe that day.

Fundamentals are read at the as-of session only (ADR-033), so a fundamentals
read cannot look ahead or reach back by construction. A build exported
without ``--fundamentals``, or with a ``ratio_version`` below 2, is refused
with :class:`FundamentalsUnavailableError` rather than read as an empty
cross-section.

Lookbacks count market sessions — every session of the dataset, whichever
instrument traded — not one instrument's own bars, so a gap in a series reads
as ``None`` in its place rather than pulling an older bar forward.
"""

from __future__ import annotations

from collections.abc import Mapping, Sequence
from datetime import date
from decimal import Decimal

from personal_quant.research.protocols import (
    Bar,
    Dataset,
    FactorEngine,
    FeatureEngine,
    FundamentalsRow,
    Lookback,
)

__all__ = [
    "MIN_RATIO_VERSION",
    "FundamentalsUnavailableError",
    "LookAheadError",
    "LookbackExceededError",
    "NonMemberScoredError",
    "PointInTimeError",
    "PointInTimeHistory",
    "evaluate",
]


MIN_RATIO_VERSION = 2
"""The oldest ``ratio_version`` a feature may read: ADR-032's rules make the ratios usable."""


class FundamentalsUnavailableError(RuntimeError):
    """A fundamentals read on a build that has none, or ratios older than version 2."""


class PointInTimeError(RuntimeError):
    """An engine used information it could not have had at its as-of."""


class LookAheadError(PointInTimeError):
    """An engine read a session after its as-of."""


class LookbackExceededError(PointInTimeError):
    """An engine read further back than the lookback it declared."""


class NonMemberScoredError(PointInTimeError):
    """An engine scored an instrument that was not a member on its as-of."""


class PointInTimeHistory:
    """One dataset as an engine may see it at one as-of."""

    def __init__(
        self, dataset: Dataset, as_of: date, lookback: Lookback, engine: str = "engine"
    ) -> None:
        """Cut a dataset off at an as-of, within a lookback.

        Args:
            dataset: The verified dataset.
            as_of: The session the engine is evaluated at.
            lookback: The lookback the engine declared.
            engine: The engine's name, so a violation says whose it was.

        Raises:
            ValueError: ``as_of`` is not one of the dataset's sessions.
        """
        sessions = list(dataset.sessions)

        try:
            index = sessions.index(as_of)
        except ValueError:
            raise ValueError(f"{as_of} is not a session of this dataset.") from None

        # Padded with None where the window reaches before the dataset begins,
        # so a window always has the length that was declared.
        first = index - lookback.sessions + 1
        self._window: tuple[date | None, ...] = tuple(
            sessions[position] if position >= 0 else None for position in range(first, index + 1)
        )
        self._dataset = dataset
        self._as_of = as_of
        self._lookback = lookback
        self._engine = engine

    @property
    def as_of(self) -> date:
        """The session the engine is evaluated at."""
        return self._as_of

    @property
    def lookback(self) -> Lookback:
        """The lookback the engine declared."""
        return self._lookback

    def members(self) -> frozenset[str]:
        """Return the universe's members on the as-of session.

        Returns:
            Their instrument ids.
        """
        return self._dataset.members_on(self._as_of)

    def bars(self, instrument_id: str, sessions: int) -> Sequence[Bar | None]:
        """Return the last ``sessions`` bars ending at the as-of, oldest first.

        Args:
            instrument_id: The instrument.
            sessions: How many sessions to read, the as-of included.

        Returns:
            One bar per session; ``None`` where the instrument has no bar or
            the session falls before the dataset begins.

        Raises:
            LookbackExceededError: More sessions were asked for than declared.
            ValueError: ``sessions`` is below one.
        """
        if sessions < 1:
            raise ValueError(f"A read covers at least one session, not {sessions}.")

        if sessions > self._lookback.sessions:
            raise LookbackExceededError(
                f"{self._engine} read {sessions} sessions at {self._as_of} but declared a "
                f"lookback of {self._lookback.sessions}."
            )

        bars = self._dataset.bars_of(instrument_id)

        return tuple(
            None if session is None else bars.get(session) for session in self._window[-sessions:]
        )

    def closes(self, instrument_id: str, sessions: int) -> Sequence[Decimal | None]:
        """Return the last ``sessions`` closes ending at the as-of, oldest first.

        Args:
            instrument_id: The instrument.
            sessions: How many sessions to read, the as-of included.

        Returns:
            One close per session; ``None`` where the instrument has no bar or
            the session falls before the dataset begins.

        Raises:
            LookbackExceededError: More sessions were asked for than declared.
            ValueError: ``sessions`` is below one.
        """
        return tuple(
            None if bar is None else bar.close for bar in self.bars(instrument_id, sessions)
        )

    def bar(self, instrument_id: str, session: date) -> Bar | None:
        """Return one bar within the window.

        Args:
            instrument_id: The instrument.
            session: The session wanted.

        Returns:
            The bar, or ``None`` when the instrument has none that session.

        Raises:
            LookAheadError: The session is after the as-of.
            LookbackExceededError: The session is before the declared window.
        """
        if session > self._as_of:
            raise LookAheadError(
                f"{self._engine} read {session}, after its as-of of {self._as_of}."
            )

        oldest = next((day for day in self._window if day is not None), self._as_of)

        if session < oldest:
            raise LookbackExceededError(
                f"{self._engine} read {session}, before its {self._lookback.sessions}-session "
                f"window starting {oldest}."
            )

        return self._dataset.bars_of(instrument_id).get(session)

    def fundamentals(self, instrument_id: str) -> FundamentalsRow | None:
        """Return the instrument's fundamentals row at the as-of.

        Args:
            instrument_id: The instrument.

        Returns:
            The row with its ``_absent`` codes, or ``None`` when the
            instrument has none that session.

        Raises:
            FundamentalsUnavailableError: The build carries no fundamentals, or
                its ``ratio_version`` is below :data:`MIN_RATIO_VERSION`.
        """
        described = self._dataset.manifest.fundamentals

        if described is None:
            raise FundamentalsUnavailableError(
                f"{self._engine} reads fundamentals, but dataset "
                f"{self._dataset.manifest.dataset_id} was exported without --fundamentals."
            )

        if described.ratio_version < MIN_RATIO_VERSION:
            raise FundamentalsUnavailableError(
                f"{self._engine} reads fundamentals at ratio_version {MIN_RATIO_VERSION} or "
                f"later; dataset {self._dataset.manifest.dataset_id} carries "
                f"{described.ratio_version}."
            )

        return self._dataset.fundamentals_of(instrument_id).get(self._as_of)


def evaluate(
    engine: FeatureEngine | FactorEngine, dataset: Dataset, as_of: date
) -> Mapping[str, float]:
    """Evaluate an engine at an as-of, through the harness.

    Args:
        engine: The feature or factor engine.
        dataset: The verified dataset.
        as_of: The session to evaluate at.

    Returns:
        The engine's values, one per instrument it could score.

    Raises:
        LookAheadError: The engine read past its as-of.
        LookbackExceededError: The engine read deeper than it declared.
        NonMemberScoredError: The engine scored a non-member.
    """
    history = PointInTimeHistory(dataset, as_of, engine.lookback, engine.name)

    values = dict(engine.compute(history))
    outsiders = sorted(set(values) - history.members())

    if outsiders:
        raise NonMemberScoredError(
            f"{engine.name} scored {', '.join(outsiders)} at {as_of}, which "
            f"{'was' if len(outsiders) == 1 else 'were'} not in the universe that day."
        )

    return values
