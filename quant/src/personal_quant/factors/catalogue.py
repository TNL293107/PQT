"""The feature catalogue (ADR-033): every entry with what it reads and when it is absent.

A run record names the features it used by ``name@version``; this registry
says what each one depended on. ``direction`` is informational: a feature is
stored in its natural sign, and the evaluation flips one where it records it.
"""

from __future__ import annotations

import math
from collections.abc import Mapping
from dataclasses import dataclass

from personal_quant.factors.liquidity import Liquidity
from personal_quant.factors.momentum import Momentum
from personal_quant.factors.quality import ReturnOnEquity
from personal_quant.factors.risk import Volatility
from personal_quant.factors.size import Size
from personal_quant.factors.value import BookToPrice, EarningsToPrice
from personal_quant.research.datasets import FUNDAMENTAL_VALUES, CanonicalDataset
from personal_quant.research.manifest import VENDOR_FLAGS_SCHEMA_VERSION
from personal_quant.research.protocols import FeatureEngine

__all__ = ["CATALOGUE", "Entry", "entry", "vendor_shares"]


@dataclass(frozen=True, slots=True)
class Entry:
    """One catalogue entry: the engine and what it declares beyond its lookback."""

    engine: FeatureEngine
    inputs: tuple[str, ...]
    absence: str
    direction: str


CATALOGUE: tuple[Entry, ...] = (
    Entry(
        Momentum(),
        ("close",),
        "No close 252 or 21 sessions ago, or a start close at or below zero.",
        "higher_is_stronger_trend",
    ),
    Entry(
        Momentum(formation=126),
        ("close",),
        "No close 126 or 21 sessions ago, or a start close at or below zero.",
        "higher_is_stronger_trend",
    ),
    Entry(
        Volatility(),
        ("close",),
        "A close missing in the 61-session window, or one at or below zero.",
        "higher_is_more_volatile",
    ),
    Entry(
        Liquidity(),
        ("turnover",),
        "A bar, or its turnover, missing in the 20-session window.",
        "higher_is_more_liquid",
    ),
    Entry(
        Size(),
        ("shares", "close_raw"),
        "No fundamentals row, shares or close_raw absent, or a close at or below zero.",
        "higher_is_larger",
    ),
    Entry(
        EarningsToPrice(),
        ("eps_ttm", "close_raw"),
        "No fundamentals row, eps_ttm or close_raw absent, or a close at or below zero.",
        "higher_is_cheaper",
    ),
    Entry(
        BookToPrice(),
        ("book_value_per_share", "close_raw"),
        "No fundamentals row, book_value_per_share (no_balance_sheet, no_charter_capital) "
        "or close_raw (no_close) absent, or a close at or below zero.",
        "higher_is_cheaper",
    ),
    Entry(
        ReturnOnEquity(),
        ("roe",),
        "No fundamentals row, or roe absent.",
        "higher_is_more_profitable",
    ),
)
"""Catalogue v1 (ADR-033 §3): eight entries."""


def entry(name: str) -> Entry:
    """Return the catalogue entry of a feature.

    Args:
        name: The feature's name.

    Returns:
        Its entry.

    Raises:
        KeyError: No feature of that name is catalogued.
    """
    for candidate in CATALOGUE:
        if candidate.engine.name == name:
            return candidate

    raise KeyError(f"No feature named {name!r} is in the catalogue.")


def vendor_shares(dataset: CanonicalDataset) -> Mapping[str, float]:
    """Return each value feature's vendor share in a build (ADR-038; 11.6 option A).

    A feature's share is, among the build's fundamentals rows where every
    fundamentals input of its entry is present, the fraction with any of those
    inputs flagged vendor. Read from the build alone: no database, no ratio
    service. A feature with no fundamentals input (momentum, volatility,
    liquidity) has no share; one whose inputs are present on no row has
    ``nan``, since there is nothing to share.

    Args:
        dataset: A verified build of schema 7 or later.

    Returns:
        The share by feature name, in catalogue order.

    Raises:
        ValueError: The build is before schema 7, so it states no flags, and a
            share of 0 would be a claim it does not make.
    """
    if dataset.manifest.schema_version < VENDOR_FLAGS_SCHEMA_VERSION:
        raise ValueError(
            f"Build {dataset.manifest.dataset_id} v{dataset.manifest.dataset_version} is schema "
            f"{dataset.manifest.schema_version}; vendor flags exist from schema "
            f"{VENDOR_FLAGS_SCHEMA_VERSION}."
        )

    rows = [
        row
        for instrument in dataset.manifest.instruments
        for row in dataset.fundamentals_of(instrument.instrument_id).values()
    ]
    shares: dict[str, float] = {}

    for candidate in CATALOGUE:
        inputs = [name for name in candidate.inputs if name in FUNDAMENTAL_VALUES]

        if not inputs:
            continue

        present = [row for row in rows if all(row.values[name] is not None for name in inputs)]
        flagged = sum(1 for row in present if row.vendor.intersection(inputs))
        shares[candidate.engine.name] = flagged / len(present) if present else math.nan

    return shares
