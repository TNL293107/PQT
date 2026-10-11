"""Signal generation.

A strategy turns factors into target positions. It never places an order:
it emits a signal, which portfolio construction sizes and the risk engine
approves or rejects before anything reaches an execution path.

Holds the cross-sectional rank and quantile portfolios of the U6 vertical
slice (:mod:`~personal_quant.strategies.signals`,
:mod:`~personal_quant.strategies.portfolio`).
"""
