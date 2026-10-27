"""The research contracts PQT owns (U6).

Each stage of the research pipeline is a PEP 544 ``Protocol``, not an abstract
base class: structural typing lets an adapter for an outside library satisfy a
contract without inheriting from PQT, and strict mypy checks the fit.

::

    Universe → Dataset → Features → Factors → Signals → Portfolio
                                                            ↓
            Experiment tracker ← Analytics ← Risk ← Backtest

**Every protocol that touches data declares a lookback and is evaluated at an
as-of.** An engine does not read a dataset; it reads a :class:`History` the
harness builds for it, cut off at the as-of session and no deeper than the
lookback it declared. Reading past either edge raises, so look-ahead is
detected rather than discouraged
(:mod:`personal_quant.research.history`).

The protocols on the momentum vertical slice have implementations; the rest
are declared here so their shape is decided once, and are implemented by the
workstreams that own them — the backtester and validator by Phase 8, the
experiment tracker by U9.
"""

from __future__ import annotations

from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import date
from decimal import Decimal
from typing import Protocol

from personal_quant.research.manifest import DatasetManifest

__all__ = [
    "Backtester",
    "Bar",
    "DataProvider",
    "Dataset",
    "ExperimentTracker",
    "FactorEngine",
    "FeatureEngine",
    "FundamentalsRow",
    "History",
    "Lookback",
    "Model",
    "PeriodReturn",
    "PortfolioConstructor",
    "ResearchValidator",
    "RiskEngine",
    "SignalEngine",
    "Strategy",
]


@dataclass(frozen=True, slots=True)
class Bar:
    """One daily bar of a canonical dataset.

    Prices are :class:`~decimal.Decimal`, read from the strings the dataset
    stores them as. Nothing between the exchange and a factor goes through a
    binary float; a factor may convert at its own boundary. ``turnover`` is
    the cash value traded (VND) as recorded, never adjusted, and ``None``
    where the source did not report it.
    """

    instrument_id: str
    session: date
    open: Decimal
    high: Decimal
    low: Decimal
    close: Decimal
    volume: int
    turnover: Decimal | None = None


@dataclass(frozen=True, slots=True)
class FundamentalsRow:
    """One row of ``fundamentals.parquet``: an instrument's ratios at one session (ADR-031).

    ``values`` maps each value column to its :class:`~decimal.Decimal`, or to
    ``None`` where it is absent; ``absent`` maps it to the dataset's reason,
    or ``None`` where it is present. ``findings`` stays the dataset's JSON text.
    """

    instrument_id: str
    session: date
    values: Mapping[str, Decimal | None]
    absent: Mapping[str, str | None]
    findings: str


@dataclass(frozen=True, slots=True)
class Lookback:
    """How many sessions an engine reads, counting the as-of session itself.

    A 20-session moving average declares ``Lookback(20)``. An engine that
    cannot state this cannot be checked against a point-in-time dataset, and
    an unchecked engine is where look-ahead lives.
    """

    sessions: int

    def __post_init__(self) -> None:
        """Refuse a lookback that reads nothing.

        Raises:
            ValueError: ``sessions`` is below one.
        """
        if self.sessions < 1:
            raise ValueError(f"A lookback reads at least the as-of session, not {self.sessions}.")


@dataclass(frozen=True, slots=True)
class PeriodReturn:
    """A return earned from one session's close to a later one's."""

    start: date
    end: date
    value: float


class Dataset(Protocol):
    """A canonical dataset build, loaded and verified."""

    @property
    def manifest(self) -> DatasetManifest:
        """The manifest the build was verified against."""
        ...

    @property
    def sessions(self) -> Sequence[date]:
        """Every session with at least one bar, oldest first."""
        ...

    def bars_of(self, instrument_id: str) -> Mapping[date, Bar]:
        """Return one instrument's bars by session."""
        ...

    def members_on(self, session: date) -> frozenset[str]:
        """Return the instruments that belonged to the universe on a session."""
        ...

    def fundamentals_of(self, instrument_id: str) -> Mapping[date, FundamentalsRow]:
        """Return one instrument's fundamentals rows by session; empty without any."""
        ...


class DataProvider(Protocol):
    """Locates versioned datasets. Never talks to a vendor."""

    def versions(self, dataset_id: str) -> Sequence[int]:
        """Return the builds held for a dataset, oldest first."""
        ...

    def open(self, dataset_id: str, version: int | None = None) -> Dataset:
        """Load and verify a build; the latest when no version is named."""
        ...


class History(Protocol):
    """What an engine may see: one dataset, up to an as-of, within a lookback."""

    @property
    def as_of(self) -> date:
        """The session the engine is evaluated at."""
        ...

    @property
    def lookback(self) -> Lookback:
        """The lookback the engine declared."""
        ...

    def members(self) -> frozenset[str]:
        """Return the universe's members on the as-of session."""
        ...

    def closes(self, instrument_id: str, sessions: int) -> Sequence[Decimal | None]:
        """Return the last ``sessions`` closes ending at the as-of, oldest first.

        ``None`` stands for a session with no bar for the instrument, or one
        before the dataset begins.
        """
        ...

    def bar(self, instrument_id: str, session: date) -> Bar | None:
        """Return one bar within the window, or ``None`` when there is none."""
        ...

    def fundamentals(self, instrument_id: str) -> FundamentalsRow | None:
        """Return the instrument's fundamentals row at the as-of, or ``None`` (ADR-033).

        Refuses a build exported without fundamentals or below ``ratio_version`` 2.
        """
        ...


class FeatureEngine(Protocol):
    """Raw columns to an engineered column, per instrument, at an as-of."""

    @property
    def name(self) -> str:
        """A stable name, recorded with every run that uses the feature."""
        ...

    @property
    def version(self) -> str:
        """Changes whenever the computation does."""
        ...

    @property
    def lookback(self) -> Lookback:
        """How far back the feature reads."""
        ...

    def compute(self, history: History) -> Mapping[str, float]:
        """Return a value per instrument; an instrument without one is absent."""
        ...


class FactorEngine(Protocol):
    """Cross-sectional, point-in-time factor values at an as-of."""

    @property
    def name(self) -> str:
        """A stable name, recorded with every run that uses the factor."""
        ...

    @property
    def version(self) -> str:
        """Changes whenever the computation does."""
        ...

    @property
    def lookback(self) -> Lookback:
        """How far back the factor reads."""
        ...

    def compute(self, history: History) -> Mapping[str, float]:
        """Return a score per member; a member without enough history is absent."""
        ...


class SignalEngine(Protocol):
    """Factor scores to a ranked signal, within one cross-section."""

    def signal(self, scores: Mapping[str, float]) -> Mapping[str, float]:
        """Return a signal per instrument scored."""
        ...


class PortfolioConstructor(Protocol):
    """Signals plus constraints to target weights."""

    def weights(self, signal: Mapping[str, float]) -> Mapping[str, float]:
        """Return target weights; an instrument not held is absent."""
        ...


class Strategy(Protocol):
    """Signals to target positions. Never places an order."""

    def target_positions(self, signal: Mapping[str, float], as_of: date) -> Mapping[str, float]:
        """Return the positions wanted at the as-of."""
        ...


class Model(Protocol):
    """``fit`` / ``predict``, shaped so scikit-learn models satisfy it as they are."""

    def fit(self, features: Sequence[Sequence[float]], labels: Sequence[float]) -> object:
        """Fit on rows of features and their labels."""
        ...

    def predict(self, features: Sequence[Sequence[float]]) -> Sequence[float]:
        """Predict one value per row."""
        ...


class Backtester(Protocol):
    """Event-driven replay under Vietnamese market rules (Phase 8)."""

    def run(self, dataset: Dataset, strategy: Strategy) -> Sequence[PeriodReturn]:
        """Replay a strategy over a dataset and return what it earned."""
        ...


class RiskEngine(Protocol):
    """Ex-ante exposure and ex-post attribution."""

    def exposures(self, weights: Mapping[str, float], as_of: date) -> Mapping[str, float]:
        """Return the exposures a set of weights carries at an as-of."""
        ...


class ResearchValidator(Protocol):
    """Walk-forward, out-of-sample, perturbation and overfitting statistics (Phase 8)."""

    def validate(self, returns: Sequence[PeriodReturn], trial_count: int) -> Mapping[str, float]:
        """Return statistics that account for how many configurations were tried."""
        ...


class ExperimentTracker(Protocol):
    """Records and retrieves reproducible runs (U9)."""

    def record(self, run: Mapping[str, object]) -> str:
        """Record a run and return its id."""
        ...

    def load(self, run_id: str) -> Mapping[str, object]:
        """Return a recorded run."""
        ...
