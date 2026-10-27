"""Factor definitions and computation.

A factor is a reproducible, point-in-time cross-sectional signal: value,
momentum, quality, and so on. Each definition must state the data it consumes
and the moment that data became knowable, so a factor computed today and the
same factor computed inside a backtest agree.

:mod:`~personal_quant.factors.catalogue` lists every feature (ADR-033) with its
inputs and absence rule; each family has its own module: momentum, risk
(volatility), liquidity, size, value (E/P, B/P) and quality (ROE).
:mod:`~personal_quant.factors.transforms` holds the cross-sectional transforms
the evaluation applies per rebalance (ADR-035).
"""
