"""Historical simulation.

The backtester replays market data in order and may only use information that
existed at the simulated moment. Avoiding look-ahead bias is the primary
correctness requirement of this package, not a refinement of it.

Holds only :mod:`~personal_quant.backtesting.returns`: frictionless
close-to-close returns for the U6 slice, which are labels, not a simulation.
The backtester itself is Phase 8's.
"""
