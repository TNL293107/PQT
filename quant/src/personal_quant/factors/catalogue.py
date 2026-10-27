"""The feature catalogue (ADR-033): every entry with what it reads and when it is absent.

A run record names the features it used by ``name@version``; this registry
says what each one depended on. ``direction`` is informational: a feature is
stored in its natural sign, and the evaluation flips one where it records it.
"""

from __future__ import annotations

from dataclasses import dataclass

from personal_quant.factors.liquidity import Liquidity
from personal_quant.factors.momentum import Momentum
from personal_quant.factors.risk import Volatility
from personal_quant.factors.size import Size
from personal_quant.research.protocols import FeatureEngine

__all__ = ["CATALOGUE", "Entry", "entry"]


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
        "No fundamentals row, or shares or close_raw absent.",
        "higher_is_larger",
    ),
)
"""Catalogue v1 so far; the value and quality entries wait on backlog P1."""


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
