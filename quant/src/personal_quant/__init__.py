"""Quantitative research layer for the Personal Quant Terminal.

This package is the Python side of the system. It reads the canonical
datasets the backend exports, verified before they are trusted, and evaluates
research engines through a point-in-time harness. It never writes the
``quant`` schema and never fetches from a market data provider (ADR-016).

* :mod:`personal_quant.research` — protocols, datasets, the harness, the slice.
* :mod:`personal_quant.factors` — factor definitions and computation.
* :mod:`personal_quant.strategies` — signal generation.
* :mod:`personal_quant.backtesting` — historical simulation.
* :mod:`personal_quant.analytics` — performance and attribution.
"""

__all__ = ["__version__"]

__version__ = "0.1.0"
