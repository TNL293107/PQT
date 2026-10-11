"""Performance and attribution.

Return series, risk-adjusted measures, drawdown analysis and attribution of
results back to the factors and trades that produced them. Shared by
backtests and by live portfolio reporting, so both are measured identically.

Holds the information coefficient and its summary
(:mod:`~personal_quant.analytics.information`); performance analytics follow
with the backtester in Phase 8.
"""
