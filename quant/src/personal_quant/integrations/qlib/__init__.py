"""Qlib, research only, behind PQT's protocols (U8, ADR-017).

The only package in PQT allowed to import Qlib; ``tests/test_qlib_confinement.py``
fails the build on an import anywhere else. Qlib is installed with the
``research`` extra and is never a base dependency: without it this package
still imports, :func:`available` says ``False``, and a call that needs the
engine raises :class:`QlibUnavailableError`.

What is taken from Qlib is its model zoo, through
:class:`~personal_quant.integrations.qlib.model.QlibLightGBM`, which satisfies
:class:`personal_quant.research.protocols.Model`. What is not taken — the
``.bin`` data layer, the backtest executor, reinforcement learning, online
serving — is not imported at all. PQT keeps its data lineage, revisions,
point-in-time semantics, instrument identity, adjustment rules, canonical
datasets and research contracts; Qlib is handed rows of numbers and returns
numbers.

Removal is three steps (``docs/architecture/qlib-integration.md``): delete this
package, remove ``pyqlib`` from the ``research`` extra, delete the guard and the
adapter's tests.
"""

from personal_quant.integrations.qlib.model import LightGBMSettings, QlibLightGBM
from personal_quant.integrations.qlib.session import (
    ENGINE,
    QlibUnavailableError,
    available,
    engine_version,
)

__all__ = [
    "ENGINE",
    "LightGBMSettings",
    "QlibLightGBM",
    "QlibUnavailableError",
    "available",
    "engine_version",
]
