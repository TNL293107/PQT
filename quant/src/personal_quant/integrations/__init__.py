"""Adapters to outside research engines, each optional and each replaceable.

An adapter satisfies a PQT protocol from :mod:`personal_quant.research.protocols`
with an engine PQT does not own. The dependency runs one way: an adapter
imports PQT, and nothing in PQT imports an adapter. Data goes in as PQT types
and plain builtins and comes out the same way, so removing an adapter removes
a capability without touching a contract.

* :mod:`personal_quant.integrations.qlib` — Microsoft Qlib's model zoo behind
  PQT's ``Model`` (U8, ADR-017). Installed with the ``research`` extra.
"""
