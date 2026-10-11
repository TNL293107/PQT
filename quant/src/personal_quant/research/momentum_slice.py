r"""The U6 vertical slice: one factor, end to end, through the research contracts.

::

    dataset → 12-1 momentum → cross-sectional rank → decile portfolios
            → return series → IC / IR

Not a library of factors and not a backtest. It exists to prove the layer:
a verified dataset goes in, the factor is evaluated through the point-in-time
harness at every rebalance, and the result records which build, factor
version and parameters produced it.

Run over an export::

    python -m personal_quant.research.momentum_slice \\
        --root data/tmp/datasets --dataset 3856e4ce77841c1a

Only a point-in-time, adjusted build is accepted. An as-of build holds one
constituent set for its whole window, so every name in it survived to the
as-of date, and the harness's membership check has nothing to catch; a raw
build scores every bonus issue as a crash. The result records the build's
membership mode, adjustment and announcement policy beside its IC, so the two
can never be read apart.

A build exported with history (``pqt dataset export --history N``, ADR-025)
holds prices from before each name's admission. The factor reads them through
the harness like any other session, so a name admitted mid-window is scored at
its first rebalance when its history covers the lookback; rebalances still
happen only inside the window, where membership is defined.

Returns are close-to-close with no costs, price-band or lot limits — see
:mod:`personal_quant.backtesting.returns` — and the IC statistics do not
account for how many factors were tried before this one, which needs the
experiment log of U9.
"""

from __future__ import annotations

import argparse
import math
from bisect import bisect_left
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import date
from pathlib import Path

from personal_quant.analytics.information import (
    InformationSummary,
    information_coefficient,
    summarise,
)
from personal_quant.backtesting.returns import forward_returns, weighted_return
from personal_quant.factors.momentum import Momentum
from personal_quant.research.datasets import LocalDatasetProvider
from personal_quant.research.history import evaluate
from personal_quant.research.protocols import (
    Dataset,
    FactorEngine,
    PeriodReturn,
    PortfolioConstructor,
    SignalEngine,
)
from personal_quant.strategies.portfolio import TopQuantile, quantile_buckets
from personal_quant.strategies.signals import CrossSectionalRank

__all__ = [
    "SlicePeriod",
    "SliceRefusedError",
    "SliceResult",
    "main",
    "render",
    "run_momentum_slice",
]


class SliceRefusedError(ValueError):
    """Raised for a dataset the slice's result could not honestly describe."""


@dataclass(frozen=True, slots=True)
class SlicePeriod:
    """One rebalance and the period it held for.

    ``bucket_returns`` runs from the lowest-signal quantile to the highest and
    is empty when the cross-section was too small to fill every quantile.
    """

    start: date
    end: date
    members: int
    scored: int
    information_coefficient: float | None
    bucket_returns: tuple[float, ...]
    top: float | None

    @property
    def spread(self) -> float | None:
        """The top quantile's return less the bottom one's."""
        if not self.bucket_returns:
            return None

        return self.bucket_returns[-1] - self.bucket_returns[0]


@dataclass(frozen=True, slots=True)
class SliceResult:
    """What the slice computed, and enough to compute it again."""

    dataset_id: str
    dataset_version: int
    content_hash: str
    universe_code: str
    membership: str
    adjusted: bool
    announcement_policy: str
    known_as_of: str | None
    factor: str
    factor_version: str
    quantiles: int
    rebalance_every: int
    periods: tuple[SlicePeriod, ...]
    information: InformationSummary

    def series(self, which: str) -> tuple[PeriodReturn, ...]:
        """Return a portfolio's return series: ``top``, ``bottom`` or ``spread``.

        Args:
            which: The portfolio.

        Returns:
            One return per period that has one.

        Raises:
            ValueError: ``which`` names no portfolio.
        """
        values: list[PeriodReturn] = []

        for period in self.periods:
            if which == "top":
                value = period.top
            elif which == "bottom":
                value = period.bucket_returns[0] if period.bucket_returns else None
            elif which == "spread":
                value = period.spread
            else:
                raise ValueError(f"'{which}' is not a portfolio of this slice.")

            if value is not None:
                values.append(PeriodReturn(period.start, period.end, value))

        return tuple(values)


def run_momentum_slice(
    dataset: Dataset,
    factor: FactorEngine | None = None,
    *,
    quantiles: int = 10,
    rebalance_every: int = 21,
    signal_engine: SignalEngine | None = None,
    portfolio: PortfolioConstructor | None = None,
) -> SliceResult:
    """Run the vertical slice over a dataset.

    Rebalances start at the first session of the window with a full lookback
    behind it and repeat every ``rebalance_every`` sessions while a whole
    period follows. A build with history may hold sessions before its window;
    they are read as lookback and never rebalanced on, since no universe is
    defined there.

    Args:
        dataset: The verified dataset.
        factor: The factor; 12-1 momentum by default.
        quantiles: Buckets per cross-section; ten for deciles.
        rebalance_every: Sessions between rebalances.
        signal_engine: Scores to signal; a cross-sectional rank by default.
        portfolio: Signal to weights; the top quantile by default.

    Returns:
        The periods, the IC summary and the run's provenance.

    Raises:
        SliceRefusedError: The build is as-of or unadjusted.
        ValueError: ``quantiles`` or ``rebalance_every`` is below one.
    """
    manifest = dataset.manifest

    if manifest.membership != "point_in_time":
        raise SliceRefusedError(
            f"{manifest.dataset_id} is an as-of build: one constituent set for the whole "
            "window, so every name survived to its as-of. Export it point-in-time."
        )

    if not manifest.adjusted:
        raise SliceRefusedError(
            f"{manifest.dataset_id} holds raw prices; momentum over them scores every bonus "
            "issue and split as a crash. Export it adjusted."
        )

    if quantiles < 1 or rebalance_every < 1:
        raise ValueError(
            f"Quantiles ({quantiles}) and the rebalance interval ({rebalance_every}) "
            "must both be at least one."
        )

    factor = factor or Momentum()
    signal_engine = signal_engine or CrossSectionalRank()
    portfolio = portfolio or TopQuantile(quantiles)
    sessions = list(dataset.sessions)
    first = max(factor.lookback.sessions - 1, bisect_left(sessions, manifest.from_date))
    periods: list[SlicePeriod] = []

    for index in range(first, len(sessions) - rebalance_every, rebalance_every):
        start, end = sessions[index], sessions[index + rebalance_every]
        scores = evaluate(factor, dataset, start)
        returns = forward_returns(dataset, start, end, scores)
        buckets: tuple[float, ...] = ()
        top: float | None = None

        if len(scores) >= quantiles:
            signal = signal_engine.signal(scores)
            measured = [
                weighted_return(dict.fromkeys(bucket, 1.0), returns)
                for bucket in quantile_buckets(signal, quantiles)
            ]
            buckets = tuple(value for value in measured if value is not None)

            if len(buckets) != quantiles:
                buckets = ()

            top = weighted_return(portfolio.weights(signal), returns)

        periods.append(
            SlicePeriod(
                start=start,
                end=end,
                members=len(dataset.members_on(start)),
                scored=len(scores),
                information_coefficient=information_coefficient(scores, returns),
                bucket_returns=buckets,
                top=top,
            )
        )

    coefficients = [
        period.information_coefficient
        for period in periods
        if period.information_coefficient is not None
    ]

    return SliceResult(
        dataset_id=manifest.dataset_id,
        dataset_version=manifest.dataset_version,
        content_hash=manifest.content_hash,
        universe_code=manifest.universe_code,
        membership=manifest.membership,
        adjusted=manifest.adjusted,
        announcement_policy=manifest.announcement_policy,
        known_as_of=manifest.known_as_of_text,
        factor=factor.name,
        factor_version=factor.version,
        quantiles=quantiles,
        rebalance_every=rebalance_every,
        periods=tuple(periods),
        information=summarise(coefficients),
    )


def render(result: SliceResult) -> str:
    """Render a result as a plain-text report.

    Args:
        result: The slice's result.

    Returns:
        The report.
    """
    lines = [
        f"Dataset   {result.dataset_id} v{result.dataset_version} "
        f"(content {result.content_hash[:16]})",
        f"Build     {result.membership}, "
        f"{'adjusted' if result.adjusted else 'raw'}, "
        f"{result.announcement_policy} announcements, "
        f"known as of {result.known_as_of or 'current'}",
        f"Factor    {result.factor} v{result.factor_version}",
        f"Quantiles {result.quantiles}, rebalanced every {result.rebalance_every} sessions",
        "",
        f"{'start':<10}  {'end':<10}  {'members':>7}  {'scored':>6}  {'IC':>7}  "
        f"{'top':>8}  {'bottom':>8}  {'spread':>8}",
    ]

    for period in result.periods:
        bottom = period.bucket_returns[0] if period.bucket_returns else None
        lines.append(
            f"{period.start}  {period.end}  {period.members:>7}  {period.scored:>6}  "
            f"{_number(period.information_coefficient):>7}  {_percent(period.top):>8}  "
            f"{_percent(bottom):>8}  {_percent(period.spread):>8}"
        )

    info = result.information
    spread = [item.value for item in result.series("spread")]
    lines += [
        "",
        f"Periods       {len(result.periods)} ({info.periods} with an IC)",
        f"IC mean       {_number(info.mean)}",
        f"IC deviation  {_number(info.deviation)}",
        f"IR            {_number(info.information_ratio)}",
        f"t-statistic   {_number(info.t_statistic)}",
        f"Top, compounded     {_percent(_compound(result.series('top')))}",
        f"Bottom, compounded  {_percent(_compound(result.series('bottom')))}",
        f"Spread > 0          {sum(value > 0 for value in spread)} of {len(spread)} periods",
        "",
        "Close-to-close, frictionless: no costs, price bands or lot sizes. Not a backtest.",
        "The statistics do not account for how many factors were tried (needs U9).",
    ]

    return "\n".join(lines)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the slice over an export and print the report.

    Args:
        argv: Command-line arguments; the process's own when ``None``.

    Returns:
        The exit code.
    """
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0] if __doc__ else None)
    parser.add_argument("--root", type=Path, required=True, help="the dataset export directory")
    parser.add_argument("--dataset", required=True, help="the dataset id")
    parser.add_argument("--version", type=int, default=None, help="the build; latest if omitted")
    parser.add_argument("--quantiles", type=int, default=10)
    parser.add_argument("--rebalance-every", type=int, default=21)
    arguments = parser.parse_args(argv)

    dataset = LocalDatasetProvider(arguments.root).open(arguments.dataset, arguments.version)
    result = run_momentum_slice(
        dataset, quantiles=arguments.quantiles, rebalance_every=arguments.rebalance_every
    )

    print(render(result))
    return 0


def _compound(series: Sequence[PeriodReturn]) -> float | None:
    if not series:
        return None

    return math.prod(1.0 + item.value for item in series) - 1.0


def _number(value: float | None) -> str:
    return "n/a" if value is None else f"{value:+.3f}"


def _percent(value: float | None) -> str:
    return "n/a" if value is None else f"{value:+.2%}"


if __name__ == "__main__":
    raise SystemExit(main())
