"""Smoke tests proving the quant environment is installed and importable."""

from __future__ import annotations

import importlib

import pytest

import personal_quant

DOMAIN_PACKAGES = [
    "personal_quant.research",
    "personal_quant.factors",
    "personal_quant.strategies",
    "personal_quant.backtesting",
    "personal_quant.analytics",
]


def test_package_exposes_a_version() -> None:
    assert personal_quant.__version__


@pytest.mark.parametrize("module_name", DOMAIN_PACKAGES)
def test_domain_package_is_importable(module_name: str) -> None:
    # Each boundary must be a real package, not a bare directory that silently
    # resolves as a namespace package and breaks packaging later.
    module = importlib.import_module(module_name)

    assert module.__doc__ is not None
