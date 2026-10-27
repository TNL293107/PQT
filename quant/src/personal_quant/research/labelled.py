"""A labelled dataset: a schema 5 build read through the request it was exported from (ADR-034).

Features and labels are not stored. They are computed at read time: each
feature through the point-in-time harness at each rebalance session, and the
label, the forward return from that close to the close ``horizon`` sessions
later, as :func:`~personal_quant.backtesting.returns.forward_returns` values it.
Absent stays absent in both.

The request is checked against the features handed in before anything is
read: one it names that is not there, or whose version or lookback differs,
refuses the build. A request that lies about a lookback sized the export's
history, so it is refused rather than trusted.
"""

from __future__ import annotations

import statistics
from collections.abc import Iterable, Iterator, Mapping, Sequence
from dataclasses import dataclass
from datetime import date

from personal_quant.backtesting.returns import forward_returns
from personal_quant.factors.catalogue import CATALOGUE
from personal_quant.research.datasets import CanonicalDataset, LocalDatasetProvider
from personal_quant.research.history import evaluate
from personal_quant.research.manifest import DatasetRequest
from personal_quant.research.protocols import FeatureEngine

__all__ = ["LabelledDataset", "LabelledDatasetError", "Rebalance", "Window"]

SAMPLE_STATEMENT = (
    "The sample is at most about 26 monthly cross-sections of at most 30 names; "
    "results on it are exploratory (ADR-033 §5)."
)


class LabelledDatasetError(RuntimeError):
    """A build that cannot be read as the labelled dataset its request describes."""


@dataclass(frozen=True, slots=True)
class Rebalance:
    """One rebalance: the features at its session and the label over its period."""

    session: date
    exit: date
    features: Mapping[str, Mapping[str, float]]
    label: Mapping[str, float]


@dataclass(frozen=True, slots=True)
class Window:
    """The window a labelled dataset scores (ADR-034 §6)."""

    first: date
    last: date
    rebalances: int
    names_min: int
    names_median: float
    names_max: int

    def statement(self) -> str:
        """Return the window as every factor report prints it."""
        return (
            f"Window {self.first} to {self.last}: {self.rebalances} rebalances, "
            f"{self.names_min}-{self.names_max} names (median {self.names_median:g}). "
            + SAMPLE_STATEMENT
        )


class LabelledDataset:
    """A verified schema 5 build and the features its request names."""

    def __init__(
        self, dataset: CanonicalDataset, request: DatasetRequest, engines: Sequence[FeatureEngine]
    ) -> None:
        """Hold a build and its checked features; use :meth:`open`."""
        self._dataset = dataset
        self._request = request
        self._engines = tuple(engines)
        self._sessions = self._rebalance_sessions()

    @staticmethod
    def open(
        provider: LocalDatasetProvider,
        dataset_id: str,
        engines: Iterable[FeatureEngine] | None = None,
        version: int | None = None,
    ) -> LabelledDataset:
        """Verify a build and check its request against the catalogue (ADR-033).

        Args:
            provider: Where the build is held.
            dataset_id: The dataset id.
            engines: The features available; the catalogue's when ``None``.
                Tests hand in their own.
            version: The build to open; the latest when ``None``.

        Returns:
            The labelled dataset.

        Raises:
            LabelledDatasetError: The manifest has no request, or a feature it
                names is missing or differs in version or lookback.
        """
        dataset = provider.open(dataset_id, version)
        request = dataset.manifest.request

        if request is None:
            raise LabelledDatasetError(
                f"Dataset {dataset_id} carries no request: it was not exported as a "
                "labelled dataset."
            )

        available = [entry.engine for entry in CATALOGUE] if engines is None else engines
        by_name = {engine.name: engine for engine in available}
        chosen: list[FeatureEngine] = []

        for feature in sorted(request.features, key=lambda feature: feature.name):
            engine = by_name.get(feature.name)

            if engine is None:
                raise LabelledDatasetError(f"The request names {feature.name}, which is not known.")

            if str(feature.version) != str(engine.version):
                raise LabelledDatasetError(
                    f"The request names {feature.name} version {feature.version}; "
                    f"this code computes version {engine.version}."
                )

            if feature.lookback != engine.lookback.sessions:
                raise LabelledDatasetError(
                    f"The request gives {feature.name} a lookback of {feature.lookback}; "
                    f"it reads {engine.lookback.sessions}."
                )

            chosen.append(engine)

        return LabelledDataset(dataset, request, chosen)

    @property
    def request(self) -> DatasetRequest:
        """The request the build was exported from."""
        return self._request

    def rebalances(self) -> Iterator[Rebalance]:
        """Yield each rebalance with its feature cross-sections and its label.

        Yields:
            One per rebalance session whose exit session is in the build.
        """
        horizon = self._request.horizon
        sessions = list(self._dataset.sessions)

        for session in self._sessions:
            # ponytail: index() per rebalance is O(n^2); fine at ~26 rebalances,
            # carry the index alongside the session if the window grows.
            exit_session = sessions[sessions.index(session) + horizon]
            members = self._dataset.members_on(session)

            yield Rebalance(
                session=session,
                exit=exit_session,
                features={
                    engine.name: evaluate(engine, self._dataset, session)
                    for engine in self._engines
                },
                label=forward_returns(self._dataset, session, exit_session, members),
            )

    def window(self) -> Window:
        """Return the window the rebalances cover (ADR-034 §6).

        Raises:
            LabelledDatasetError: No rebalance fits in the build.
        """
        if not self._sessions:
            raise LabelledDatasetError(
                "No rebalance session has a whole label period in this build."
            )

        names = [len(self._dataset.members_on(session)) for session in self._sessions]

        return Window(
            first=self._sessions[0],
            last=self._sessions[-1],
            rebalances=len(self._sessions),
            names_min=min(names),
            names_median=statistics.median(names),
            names_max=max(names),
        )

    def _rebalance_sessions(self) -> tuple[date, ...]:
        # The first session on or after `from`, then every rebalance_every
        # sessions; one whose t + horizon passes the last session is dropped.
        sessions = list(self._dataset.sessions)
        scored = [
            index
            for index, session in enumerate(sessions)
            if self._request.from_date <= session <= self._request.to_date
        ]

        if not scored:
            return ()

        return tuple(
            sessions[index]
            for index in range(scored[0], scored[-1] + 1, self._request.rebalance_every)
            if index + self._request.horizon < len(sessions)
        )
