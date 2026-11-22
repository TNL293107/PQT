"""The Phase 7 factor report's tooling: its request, its runs and its tables.

Sprint 10 kept these under the git-ignored ``tmp/`` and lost them; sprint 11.6
keeps them here so a refresh is a re-run (``tools/phase-7/README.md``):

- :func:`build_request` writes the labelled-dataset request (ADR-034) from the
  catalogue, so every name, version and lookback is the code's own;
- :func:`read_runs` reads an experiment's runs and their metrics in one
  read-only transaction, and :func:`runs_to_jsonl` keeps what it read;
- :func:`render_tables` turns those runs into the report's tables, so no number
  in the report is typed by hand;
- :func:`render_vendor_shares` states, per value feature, the share of its
  inputs a vendor supplied, read from the dataset build alone (ADR-038).

The tables list factors in catalogue order and never rank them, and the count
line states every trial, failed ones included (ADR-033 §5, ADR-036).
"""

from __future__ import annotations

import json
import math
from collections.abc import Iterable, Mapping, Sequence
from dataclasses import asdict, dataclass, field
from datetime import date
from typing import Any

import psycopg
from psycopg import sql

from personal_quant.factors.catalogue import CATALOGUE, vendor_shares
from personal_quant.research.datasets import CanonicalDataset

__all__ = [
    "ReportError",
    "ReportRun",
    "build_request",
    "read_runs",
    "render_tables",
    "render_vendor_shares",
    "runs_from_jsonl",
    "runs_to_jsonl",
]

_TRANSFORMS = ("raw", "wz-1")
# The payload keys the worker records as a run's params (handlers.py).
_TRANSFORM = "transform_version"
_EVALUATION = "factor_evaluation"
_CORRELATION = "factor_correlation"


class ReportError(ValueError):
    """The request or the runs cannot make the report as stated."""


@dataclass(frozen=True, slots=True)
class ReportRun:
    """One recorded run, as the report reads it from ``research.runs``."""

    run_id: str
    experiment: str
    strategy: str
    status: str
    dataset_id: str
    dataset_version: int
    schema_version: int
    content_hash: str
    factor_version: str
    params: Mapping[str, Any]
    code_commit: str | None
    trial_index: int | None
    trial_count: int | None
    reproduces_run_id: str | None
    job_id: str | None
    metrics: Mapping[str, float | None] = field(default_factory=dict)


# --- the request ---------------------------------------------------------------


def build_request(
    *,
    universe: str,
    from_date: date,
    to_date: date,
    rebalance_every: int = 21,
    horizon: int = 21,
) -> dict[str, Any]:
    """Return a ``labelled-dataset-request-v1`` naming every catalogue feature.

    Raises:
        ReportError: The window is empty, or the horizon is not the rebalance
            (ADR-034 v1: labels neither overlap nor leave gaps).
    """
    if to_date <= from_date:
        raise ReportError(f"The request ends on {to_date}, not after it starts on {from_date}.")

    if horizon != rebalance_every:
        raise ReportError(
            f"A horizon of {horizon} differs from the rebalance of {rebalance_every}; "
            "ADR-034 v1 requires them equal."
        )

    features = sorted(
        (
            {
                "name": entry.engine.name,
                "version": int(entry.engine.version),
                "lookback": entry.engine.lookback.sessions,
            }
            for entry in CATALOGUE
        ),
        key=lambda feature: str(feature["name"]),
    )
    return {
        "universe": universe,
        "as_of": None,
        "from": from_date.isoformat(),
        "to": to_date.isoformat(),
        "interval": "1d",
        "fundamentals": True,
        "rebalance_every": rebalance_every,
        "features": features,
        "label": {"horizon": horizon, "basis": "adjusted"},
    }


# --- the runs ------------------------------------------------------------------

_RUNS = """
SELECT r.id::text, e.name, r.strategy, r.status, r.dataset_id, r.dataset_version,
       r.schema_version, r.content_hash, r.factor_version, r.params, r.code_commit,
       r.trial_index, r.trial_count, r.reproduces_run_id::text, r.job_id::text,
       coalesce(
           (SELECT jsonb_object_agg(m.name, m.value) FROM {metrics} m
             WHERE m.run_id = r.id AND m.split = 'all'),
           '{{}}'::jsonb)
  FROM {runs} r
  JOIN {experiments} e ON e.id = r.experiment_id
 WHERE e.name = %(experiment)s OR e.name = %(experiment)s || '-correlation'
 ORDER BY e.name, r.trial_index NULLS LAST, r.started_at_utc, r.id
"""


def read_runs(
    connection: psycopg.Connection[Any], experiment: str, schema: str = "research"
) -> list[ReportRun]:
    """Read an experiment's runs, and its correlation experiment's, read-only.

    The transaction is read-only and rolled back, so the report can never
    write to the run store.
    """
    query = sql.SQL(_RUNS).format(
        runs=sql.Identifier(schema, "runs"),
        experiments=sql.Identifier(schema, "experiments"),
        metrics=sql.Identifier(schema, "run_metrics"),
    )

    try:
        connection.execute("BEGIN READ ONLY")
        rows = connection.execute(query, {"experiment": experiment}).fetchall()
    finally:
        connection.rollback()

    return [
        ReportRun(
            run_id=row[0],
            experiment=row[1],
            strategy=row[2],
            status=row[3],
            dataset_id=row[4],
            dataset_version=row[5],
            schema_version=row[6],
            content_hash=row[7],
            factor_version=row[8],
            params=row[9],
            code_commit=row[10],
            trial_index=row[11],
            trial_count=row[12],
            reproduces_run_id=row[13],
            job_id=row[14],
            metrics=dict(sorted(row[15].items())),
        )
        for row in rows
    ]


def runs_to_jsonl(runs: Iterable[ReportRun]) -> str:
    """One JSON object a line, keys sorted, so two reads diff line by line."""
    return "".join(json.dumps(asdict(run), sort_keys=True) + "\n" for run in runs)


def runs_from_jsonl(text: str) -> list[ReportRun]:
    """Read what :func:`runs_to_jsonl` wrote."""
    return [ReportRun(**json.loads(line)) for line in text.splitlines() if line.strip()]


# --- the tables ----------------------------------------------------------------


def render_tables(runs: Sequence[ReportRun], experiment: str) -> str:
    """Render the report's tables from an experiment's recorded runs.

    Raises:
        ReportError: The experiment has no runs, or its runs read more than one
            dataset build, so no window statement covers them all.
    """
    own = [run for run in runs if run.experiment == experiment]
    trials = sorted((run for run in own if run.trial_index is not None), key=_trial)
    reproductions = [run for run in own if run.reproduces_run_id is not None]
    correlations = [
        run
        for run in runs
        if run.experiment == f"{experiment}-correlation" and run.strategy == _CORRELATION
    ]

    if not trials:
        raise ReportError(f"Experiment {experiment} has no runs.")

    builds = {
        (run.dataset_id, run.dataset_version, run.content_hash) for run in [*own, *correlations]
    }

    if len(builds) != 1:
        raise ReportError(
            f"Experiment {experiment}'s runs read more than one dataset build: "
            + ", ".join(sorted(f"{d} v{v}" for d, v, _ in builds))
        )

    count = max(run.trial_count or 0 for run in trials)
    sections = [
        _count_section(experiment, own, trials, reproductions, count, correlations),
        _feature_section(trials),
        _decay_section(trials),
        _correlation_section(correlations),
        _run_id_section(trials, reproductions),
    ]
    return "\n\n".join(sections) + "\n"


def render_vendor_shares(dataset: CanonicalDataset) -> str:
    """Render the vendor share of each value feature's inputs, from one build.

    One row per catalogue entry with a fundamentals input, in catalogue order:
    its inputs and :func:`~personal_quant.factors.catalogue.vendor_shares`'
    share. A feature whose inputs are present on no row has nothing to share
    and reads "—", never 0. The heading names the build (id, version, schema,
    content hash) and its ``vendor_rows``, so the table is tied to the dataset
    the runs read.

    Raises:
        ValueError: The build is before schema 7 and states no flags
            (``vendor_shares``' own refusal, raised through).
    """
    shares = vendor_shares(dataset)
    manifest = dataset.manifest
    fundamentals = manifest.fundamentals
    rows_line = (
        f"{fundamentals.vendor_rows} of {fundamentals.rows} fundamentals rows "
        "rest on a vendor value"
        if fundamentals is not None
        else "no fundamentals"
    )
    lines = [
        "## Vendor share per value feature",
        "",
        f"Dataset `{manifest.dataset_id}` v{manifest.dataset_version}, "
        f"schema {manifest.schema_version}, content hash `{manifest.content_hash}`; "
        f"{rows_line} (ADR-038).",
        "",
        "| feature | inputs | vendor share |",
        "| --- | --- | --- |",
    ]

    for entry in CATALOGUE:
        name = entry.engine.name

        if name not in shares:
            continue

        share = shares[name]
        cell = "—" if math.isnan(share) else f"{share:.4f}"
        lines.append(f"| {name} | {', '.join(entry.inputs)} | {cell} |")

    return "\n".join(lines) + "\n"


def _count_section(
    experiment: str,
    own: Sequence[ReportRun],
    trials: Sequence[ReportRun],
    reproductions: Sequence[ReportRun],
    count: int,
    correlations: Sequence[ReportRun],
) -> str:
    first = trials[0]
    correlation_runs = "run" if len(correlations) == 1 else "runs"
    failed = sum(run.status == "failed" for run in own)
    unqueued = sum(run.job_id is None for run in own)
    reproduced = "reproduction" if len(reproductions) == 1 else "reproductions"
    return "\n".join(
        [
            f"**`trial_count` = {count} for every number in these tables.**",
            "",
            "| | |",
            "| --- | --- |",
            f"| Dataset | `{first.dataset_id}` v{first.dataset_version}, schema "
            f"{first.schema_version}, content hash `{first.content_hash}` |",
            f"| Experiment | `{experiment}`: {len(own)} runs, made of {len(trials)} trials and "
            f"{len(reproductions)} {reproduced}; latest `trial_count` {count}; {failed} failed; "
            f"{unqueued} without a job |",
            # QA PR #34 M1: the correlation runs' failures and missing jobs count too.
            f"| Correlation | `{experiment}-correlation`: {len(correlations)} {correlation_runs}; "
            f"{sum(run.status == 'failed' for run in correlations)} failed; "
            f"{sum(run.job_id is None for run in correlations)} without a job |",
            "| Code | "
            + ", ".join(sorted({f"`{run.code_commit}`" for run in [*own, *correlations]}))
            + " |",
        ]
    )


def _feature_section(trials: Sequence[ReportRun]) -> str:
    header = (
        "| trial | feature | names (mean) | rank IC mean | t | IC mean | t | spread mean | t "
        "| turnover mean | periods (IC / spread / turnover) |\n"
        "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"
    )
    parts = ["## Per feature"]

    for transform in _TRANSFORMS:
        rows = [_feature_row(run) for run in _in_catalogue_order(trials, transform)]
        parts.append(f"### `{transform}`\n\n{header}\n" + "\n".join(rows))

    return "\n\n".join(parts)


def _feature_row(run: ReportRun) -> str:
    lead = f"| {run.trial_index} | {run.params['feature']} |"

    if run.status != "succeeded":
        return f"{lead} {run.status} |"

    m = run.metrics
    cells = [
        _fixed(m.get("coverage_mean"), 1),
        _signed(m.get("rank_ic_mean"), 4),
        _signed(m.get("rank_ic_t_statistic"), 2),
        _signed(m.get("ic_mean"), 4),
        _signed(m.get("ic_t_statistic"), 2),
        _signed(m.get("spread_mean"), 4),
        _signed(m.get("spread_t_statistic"), 2),
        _fixed(m.get("turnover_mean"), 4),
        " / ".join(
            _whole(m.get(name))
            for name in ("rank_ic_periods", "spread_periods", "turnover_periods")
        ),
    ]
    return f"{lead} " + " | ".join(cells) + " |"


def _decay_section(trials: Sequence[ReportRun]) -> str:
    horizons = sorted(
        {
            int(name.removeprefix("decay_periods_h"))
            for run in trials
            for name in run.metrics
            if name.startswith("decay_periods_h")
        }
    )
    header = (
        "| feature | " + " | ".join(f"h={h}" for h in horizons) + " |\n"
        "| --- | " + " | ".join("---" for _ in horizons) + " |"
    )
    parts = ["## Decay: mean rank IC (periods)"]

    for transform in _TRANSFORMS:
        rows = [_decay_row(run, horizons) for run in _in_catalogue_order(trials, transform)]
        parts.append(f"### `{transform}`\n\n{header}\n" + "\n".join(rows))

    return "\n\n".join(parts)


def _decay_row(run: ReportRun, horizons: Sequence[int]) -> str:
    # A trial that did not succeed is said with its status, never dropped.
    if run.status != "succeeded":
        return f"| {run.params['feature']} | {run.status} |"

    return (
        f"| {run.params['feature']} | "
        + " | ".join(
            f"{_signed(run.metrics.get(f'decay_rank_ic_mean_h{h}'), 4)} "
            f"({_whole(run.metrics.get(f'decay_periods_h{h}'))})"
            for h in horizons
        )
        + " |"
    )


def _correlation_section(correlations: Sequence[ReportRun]) -> str:
    parts = ["## Correlation between features"]

    if not correlations:
        parts.append("No correlation run was recorded for this experiment.")

    for run in correlations:
        if run.status != "succeeded":
            parts.append(
                f"The correlation run under `{run.params.get(_TRANSFORM)}`: {run.status} "
                f"(run `{run.run_id}`); it has no pairs to show."
            )
            continue

        pairs = sorted(
            name.removeprefix("corr_").removesuffix("_mean").split("__")
            for name in run.metrics
            if name.startswith("corr_") and name.endswith("_mean")
        )
        rows = [
            f"| {left} / {right} | {_signed(run.metrics.get(f'corr_{left}__{right}_mean'), 4)} "
            f"| {_whole(run.metrics.get(f'corr_{left}__{right}_periods'))} |"
            for left, right in pairs
        ]
        parts.append(
            "The mean of the per-rebalance Spearman correlations under "
            f"`{run.params.get(_TRANSFORM)}` (not pooled), with its period count, "
            "in name order.\n\n| pair | mean | periods |\n| --- | --- | --- |\n" + "\n".join(rows)
        )

    return "\n\n".join(parts)


def _run_id_section(trials: Sequence[ReportRun], reproductions: Sequence[ReportRun]) -> str:
    rows = [
        f"| {run.trial_index} | {run.params.get('feature')} | {run.params.get(_TRANSFORM)} "
        f"| `{run.run_id}` | {run.status} | `{run.code_commit}` |"
        for run in trials
    ]
    by_id = {run.run_id: run.trial_index for run in trials}
    rows += [
        f"| — | {run.params.get('feature')} | {run.params.get(_TRANSFORM)} | `{run.run_id}` "
        f"| {run.status}, reproduces trial {by_id.get(run.reproduces_run_id or '', '?')} "
        f"| `{run.code_commit}` |"
        for run in reproductions
    ]
    return (
        "## Run ids\n\n| trial | feature | transform | run id | status | commit |\n"
        "| --- | --- | --- | --- | --- | --- |\n" + "\n".join(rows)
    )


def _in_catalogue_order(trials: Sequence[ReportRun], transform: str) -> list[ReportRun]:
    order = {entry.engine.name: index for index, entry in enumerate(CATALOGUE)}
    chosen = [
        run
        for run in trials
        if run.strategy == _EVALUATION and run.params.get(_TRANSFORM) == transform
    ]
    return sorted(
        chosen, key=lambda run: (order.get(str(run.params["feature"]), len(order)), _trial(run))
    )


def _trial(run: ReportRun) -> int:
    return run.trial_index if run.trial_index is not None else 0


def _signed(value: float | None, digits: int) -> str:
    return "absent" if value is None else f"{value:+.{digits}f}"


def _fixed(value: float | None, digits: int) -> str:
    return "absent" if value is None else f"{value:.{digits}f}"


def _whole(value: float | None) -> str:
    return "absent" if value is None else str(int(value))
