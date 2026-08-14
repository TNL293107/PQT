"""Signal generation.

A strategy turns factors into target positions. It never places an order:
it emits a signal, which portfolio construction sizes and the risk engine
approves or rejects before anything reaches an execution path.

Phase 0 status: empty. Populated from Phase 8 — Quant Research Framework.
"""
