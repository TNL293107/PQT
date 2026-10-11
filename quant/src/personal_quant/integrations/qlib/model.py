"""Qlib's LightGBM model behind PQT's ``Model`` protocol (U8, ADR-017).

:class:`QlibLightGBM` satisfies :class:`personal_quant.research.protocols.Model`
— ``fit(features, labels)`` and ``predict(features)`` over rows of floats —
with ``qlib.contrib.model.gbdt.LGBModel`` doing the work.

Qlib's models are fed a ``DatasetH``, which normally sits on Qlib's data
layer: a ``.bin`` store read through a handler's expression engine. That layer
is exactly what ADR-017 keeps out, because it carries none of the provenance,
revision or point-in-time guarantees PQT's datasets do. So the model is
handed :class:`_Segments` instead, an in-memory stand-in that answers the two
things ``LGBModel`` asks of a dataset — its ``segments`` and ``prepare`` — from
the rows PQT built. Which rows those are, at what as-of and with which labels,
is decided before the adapter is called, by PQT, through the point-in-time
harness. Qlib sees numbers, not instruments or sessions.

Nothing from Qlib, pandas or NumPy crosses back: predictions are a tuple of
builtin floats, the training curve a mapping of builtin floats, and
:meth:`QlibLightGBM.describe` returns builtins a run record can store as they
are. Removing Qlib removes this class and nothing that calls the protocol.

Rows are checked before Qlib is touched — and whether or not it is
installed: not empty, not ragged, one label per row, and every value finite.
LightGBM would take a NaN as "missing" and route it down a learned branch;
what a missing feature means is PQT's to decide when it builds the rows, not
an engine default to inherit by accident.

Reproducibility: LightGBM runs single-threaded and ``deterministic`` with the
seed in :class:`LightGBMSettings`, because thread count is a common and silent
source of drift between two runs of the same configuration.
"""

from __future__ import annotations

import math
import warnings
from collections.abc import Mapping, Sequence
from dataclasses import asdict, dataclass
from importlib.metadata import version
from pathlib import Path
from types import MappingProxyType
from typing import Any

from personal_quant.integrations.qlib.session import (
    ENGINE,
    QlibUnavailableError,
    available,
    default_scratch,
    engine_version,
    recording,
)

__all__ = ["LightGBMSettings", "QlibLightGBM"]

_ENGINE_MODEL = "qlib.contrib.model.gbdt.LGBModel"

# With no validation segment LightGBM has nothing to stop early on, says so in
# a UserWarning, and boosts for num_boost_round rounds — which the settings
# record. The warning describes a choice the caller made, so it is not raised.
_NO_VALIDATION = "Only training set found, disabling early stopping"


@dataclass(frozen=True, slots=True)
class LightGBMSettings:
    """Everything that decides what the model learns, recorded with each run.

    Attributes:
        num_boost_round: Boosting rounds, at most.
        early_stopping_rounds: Rounds without improvement on the validation
            rows before boosting stops; unused without validation rows.
        learning_rate: Shrinkage per round.
        num_leaves: Leaves per tree.
        min_data_in_leaf: Rows a leaf must hold.
        seed: LightGBM's seed.
        num_threads: Threads; one, so a run is repeatable.
    """

    num_boost_round: int = 200
    early_stopping_rounds: int = 20
    learning_rate: float = 0.05
    num_leaves: int = 31
    min_data_in_leaf: int = 20
    seed: int = 0
    num_threads: int = 1

    def __post_init__(self) -> None:
        """Refuse settings that cannot train a model.

        Raises:
            ValueError: A count is below one or the learning rate is not positive.
        """
        counts = {
            "num_boost_round": self.num_boost_round,
            "early_stopping_rounds": self.early_stopping_rounds,
            "num_leaves": self.num_leaves,
            "min_data_in_leaf": self.min_data_in_leaf,
            "num_threads": self.num_threads,
        }

        for name, value in counts.items():
            if value < 1:
                raise ValueError(f"{name} must be at least one, not {value}.")

        if not self.learning_rate > 0:
            raise ValueError(f"learning_rate must be positive, not {self.learning_rate}.")


class QlibLightGBM:
    """Qlib's ``LGBModel``, fitted and queried through PQT's ``Model`` protocol."""

    def __init__(
        self, settings: LightGBMSettings | None = None, *, scratch: Path | None = None
    ) -> None:
        """Configure the model; Qlib is not touched until :meth:`fit`.

        Args:
            settings: The model's settings; the defaults when ``None``.
            scratch: Where Qlib's recorder may write; a directory under the
                system's temporary directory when ``None``. Never a source of
                truth, and never read back.
        """
        self._settings = settings or LightGBMSettings()
        self._scratch = scratch or default_scratch()
        self._engine: Any = None
        self._width: int | None = None
        self._curve: Mapping[str, tuple[float, ...]] = MappingProxyType({})
        self._fits = 0

    @property
    def settings(self) -> LightGBMSettings:
        """The settings the model trains with."""
        return self._settings

    @property
    def training_curve(self) -> Mapping[str, tuple[float, ...]]:
        """The last fit's metric per round, keyed ``<metric>.<segment>``; empty before one."""
        return self._curve

    def fit(
        self,
        features: Sequence[Sequence[float]],
        labels: Sequence[float],
        *,
        validation: tuple[Sequence[Sequence[float]], Sequence[float]] | None = None,
    ) -> QlibLightGBM:
        """Fit on rows of features and their labels.

        Args:
            features: One row per observation, every row the same width.
            labels: One label per row.
            validation: Rows and labels held out for early stopping; without
                them, boosting runs every round in the settings.

        Returns:
            The model, fitted.

        Raises:
            ValueError: The rows are empty, ragged, non-finite, or do not
                match their labels.
            QlibUnavailableError: Qlib is not installed.
        """
        width = _width_of(features, labels, "training")
        segments: dict[str, tuple[Sequence[Sequence[float]], Sequence[float] | None]] = {
            "train": (features, labels),
        }

        if validation is not None:
            held_out, held_out_labels = validation

            if _width_of(held_out, held_out_labels, "validation") != width:
                raise ValueError("Validation rows must be as wide as the training rows.")

            segments["valid"] = validation

        if not available():
            raise QlibUnavailableError

        from qlib.contrib.model.gbdt import LGBModel

        settings = self._settings
        engine = LGBModel(
            loss="mse",
            early_stopping_rounds=settings.early_stopping_rounds,
            num_boost_round=settings.num_boost_round,
            learning_rate=settings.learning_rate,
            num_leaves=settings.num_leaves,
            min_data_in_leaf=settings.min_data_in_leaf,
            seed=settings.seed,
            num_threads=settings.num_threads,
            deterministic=True,
        )
        curve: dict[str, dict[str, list[float]]] = {}
        self._fits += 1

        with (
            recording(self._scratch, f"lightgbm-{id(self):x}-{self._fits}"),
            warnings.catch_warnings(),
        ):
            warnings.filterwarnings("ignore", message=_NO_VALIDATION, category=UserWarning)
            engine.fit(_Segments(segments, width), verbose_eval=0, evals_result=curve)

        self._engine = engine
        self._width = width
        self._curve = MappingProxyType(
            {
                f"{metric}.{segment}": tuple(float(value) for value in values)
                for segment, metrics in curve.items()
                for metric, values in metrics.items()
            }
        )

        return self

    def predict(self, features: Sequence[Sequence[float]]) -> tuple[float, ...]:
        """Predict one value per row.

        Args:
            features: Rows as wide as the ones the model was fitted on.

        Returns:
            One prediction per row, in row order.

        Raises:
            ValueError: The model is not fitted, or the rows are empty,
                ragged, non-finite or of the wrong width.
        """
        if self._engine is None or self._width is None:
            raise ValueError("The model has not been fitted.")

        width = _width_of(features, None, "prediction")

        if width != self._width:
            raise ValueError(
                f"The model was fitted on {self._width} features; these rows have {width}."
            )

        predicted = self._engine.predict(_Segments({"test": (features, None)}, width), "test")

        return tuple(float(value) for value in predicted.to_numpy())

    def describe(self) -> Mapping[str, object]:
        """Return what a run record needs to say which engine produced a result.

        Returns:
            ``research_engine``, ``research_engine_version``, the engine's
            model class, the LightGBM it trained with — a different LightGBM
            can grow different trees from the same settings — and the
            settings, all as builtins.

        Raises:
            QlibUnavailableError: Qlib is not installed.
        """
        return {
            "research_engine": ENGINE,
            "research_engine_version": engine_version(),
            "engine_model": _ENGINE_MODEL,
            "lightgbm_version": version("lightgbm"),
            "settings": asdict(self._settings),
        }


class _Segments:
    """What ``LGBModel`` asks of a ``DatasetH``, answered from PQT's rows.

    Not a Qlib type and not a subclass of one: ``LGBModel`` reads only
    ``segments`` and ``prepare``, and duck typing is how it is satisfied
    without importing Qlib's data layer at all.
    """

    def __init__(
        self,
        rows: Mapping[str, tuple[Sequence[Sequence[float]], Sequence[float] | None]],
        width: int,
    ) -> None:
        self._rows = rows
        self._width = width

    @property
    def segments(self) -> Mapping[str, None]:
        return dict.fromkeys(self._rows)

    def prepare(self, segment: str, col_set: str | Sequence[str], data_key: object = None) -> Any:  # noqa: ANN401 - a pandas frame, which never leaves the adapter.
        import pandas as pd

        features, labels = self._rows[segment]
        columns = pd.MultiIndex.from_tuples(
            [("feature", f"f{index}") for index in range(self._width)]
        )
        frame = pd.DataFrame([list(row) for row in features], columns=columns, dtype="float64")

        if col_set == "feature":
            return frame

        if labels is None:
            raise ValueError(f"The {segment} segment has no labels.")

        label = pd.DataFrame(
            {("label", "LABEL0"): list(labels)}, index=frame.index, dtype="float64"
        )

        return pd.concat([frame, label], axis=1)


def _width_of(
    features: Sequence[Sequence[float]], labels: Sequence[float] | None, what: str
) -> int:
    if not features:
        raise ValueError(f"The {what} rows are empty.")

    width = len(features[0])

    if width == 0:
        raise ValueError(f"The {what} rows have no features.")

    for index, row in enumerate(features):
        if len(row) != width:
            raise ValueError(
                f"{what.capitalize()} row {index} has {len(row)} features, not {width}."
            )

        if not all(math.isfinite(value) for value in row):
            raise ValueError(f"{what.capitalize()} row {index} holds a value that is not finite.")

    if labels is not None:
        if len(labels) != len(features):
            raise ValueError(f"{len(features)} {what} rows were given {len(labels)} labels.")

        if not all(math.isfinite(value) for value in labels):
            raise ValueError(f"A {what} label is not finite.")

    return width
