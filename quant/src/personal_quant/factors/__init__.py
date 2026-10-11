"""Factor definitions and computation.

A factor is a reproducible, point-in-time cross-sectional signal: value,
momentum, quality, and so on. Each definition must state the data it consumes
and the moment that data became knowable, so a factor computed today and the
same factor computed inside a backtest agree.

Holds :mod:`~personal_quant.factors.momentum` (12-1 momentum), the factor of
the U6 vertical slice. A library of factors is Phase 7's.
"""
