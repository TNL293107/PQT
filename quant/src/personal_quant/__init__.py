"""Quantitative research layer for the Personal Quant Terminal.

This package is the Python side of the system. It is deliberately empty of
financial logic in Phase 0: it exists so the environment, packaging, linting
and test tooling are proven before any research code is written.

Subpackages mark the boundaries the layer will grow into:

* :mod:`personal_quant.research` — exploratory analysis and notebooks.
* :mod:`personal_quant.factors` — factor definitions and computation.
* :mod:`personal_quant.strategies` — signal generation.
* :mod:`personal_quant.backtesting` — historical simulation.
* :mod:`personal_quant.analytics` — performance and attribution.
"""

__all__ = ["__version__"]

__version__ = "0.1.0"
