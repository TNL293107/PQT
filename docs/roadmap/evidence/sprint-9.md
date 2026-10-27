# Evidence: Sprint 9 (Phase 7 foundations, ADR-033 and ADR-034)

Drafted 2026-10-27 by QA + QC from `git log` on `main` (`e441dcc`), the branch
`feature/phase-7-labelled-dataset-code` (`6087385`) and the Tester + Audit gate
outputs in `tmp/audit-<sha>/`. Each fact names its source. **UNVERIFIED** marks
what no command output or gate file shows.

How to read the gates: `pytest.txt` and `dotnet-test.txt` are full suites at the
audited commit; `negative_control*.txt` lists planted mutants and whether the
suite (and a separate hand check, where there is one) killed them. No gate
folder holds a written PASS/FAIL verdict; the verdicts below are therefore
**UNVERIFIED** as written, and stand on Lead's merge messages in the room.

## 9.1 Window stated, not widened

- VN30 is unchanged (Moderator decision 3, `sprints-8-10-plan.md`).
- The window is stated by the reader: `Window.statement()` in
  `quant/src/personal_quant/research/labelled.py` prints first and last
  scored session, rebalance count, names min/median/max and
  `SAMPLE_STATEMENT` ("at most about 26 monthly cross-sections of at most 30
  names; results on it are exploratory (ADR-033 §5)"). Tested by
  `test_the_window_states_its_rebalances_and_names` (`test_labelled.py`).
- Not on `main` yet: it lands with 9.2 below.
- The window over a real export (actual first/last session and counts):
  **UNVERIFIED** — no real labelled export has been run.

## 9.2 The labelled-dataset spec resolves to a manifest (ADR-034, schema 5)

ADR-034 accepted on `main` (`2b95793`); request builder deferred to sprint 10
(`e441dcc`).

Commits on `feature/phase-7-labelled-dataset-code`, rebased onto `main` (not
merged, not pushed):

| commit | what |
| --- | --- |
| `917c9ed` (was `0ec5015`) | QA + QC acceptance fixture: `v5-request.json` + hand-rendered `v5-request.canonical.txt` |
| `5015301` (was `5d9aa39`) | schema 5 in C# and Python, `--request` on export, `LabelledDataset` |
| `26b0482` (was `5549e04`) | drop-rule test, `v5-export.json` named |
| `6087385` (was `1b10d7c`) | one-past-the-end drop, absent label, ordinal order; `ponytail:` on `rebalances()` |

The rebase did not change `labelled.py`, `manifest.py` or
`backend/src/.../Datasets` (`git diff 1b10d7c 6087385` on those paths: empty).

Hash parity: the hand-built `v5-request.json` and the .NET-written
`v5-export.json` verify in both languages (QA + QC run at `1b10d7c`:
`test_dataset_manifest.py` + `test_labelled.py` 59 passed; dotnet filter
`DatasetManifest|DatasetCommands|DatasetExportService` 73 passed at `5d9aa39`).

Gates (Tester + Audit):

| sha | Python | .NET unit | integration | mutants killed by tests |
| --- | --- | --- | --- | --- |
| `5549e04` | 305 passed, 14 skipped | 1,282 | 206 | C# 2 of 3 valid (`cs_culture_sort` SURVIVED; `cs_allow_horizon_ne_rebalance` did not build); Python 4 of 6 (`py_keep_rebalance_exit_past_to`, `py_missing_exit_close_is_zero` SURVIVED, both killed by the hand check) |
| `1b10d7c` | 308 passed, 14 skipped | 1,283 | 206 | C# 4 of 4; Python 6 of 6 |

Gate verdict at `1b10d7c`: **UNVERIFIED** (no verdict file; every mutant killed).
`CATALOGUE` wiring in `LabelledDataset` (Lead's one-liner): not in `6087385`
(`git grep CATALOGUE` on `labelled.py`: none).

## 9.3 Factors registered (ADR-033 catalogue v1)

Merged to `main` in `0573690` and pushed (`bf0080d..5f49ad4`). Eight entries
in `CATALOGUE` (`factors/catalogue.py`): `momentum_12_1`, `momentum_6_1`,
`volatility_60`, `liquidity_20`, `size`, `value_ep`, `value_bp`, `quality_roe`.

| sha | what | Python | mutants killed by tests |
| --- | --- | --- | --- |
| `ba24601` | bar turnover in reader and DuckDB view (now `fda80f4`) | 284 passed, 25 skipped | 3 of 3 |
| `abd8cad` | as-of fundamentals row through the harness (now `b72cdfe`) | 300 passed, 14 skipped | 5 of 6 (`p1_forward_fills_last_row` SURVIVED) |
| `a72844a` | momentum 6-1, volatility, liquidity, size | 316 passed, 14 skipped | 5 of 6 (`liq_mean_not_median` SURVIVED) |
| `cb3a7e0` | liquidity test fixed | 316 passed, 14 skipped | 6 of 6 |
| `f2f6d1f` | value_ep, value_bp, quality_roe | 331 passed, 14 skipped | 6 of 6 |
| `4e73a34` | size absent at close ≤ 0; share-count caveats | 337 passed, 14 skipped | 6 of 6 |

QA + QC review of `f2f6d1f`: 4 minor, 0 blocker; all four closed in
`87417d7` and `4e73a34`.

Whether `p1_forward_fills_last_row` (survived at `abd8cad`) is killed by a
later commit: **UNVERIFIED** — no later gate re-ran that mutant.

Suites on merged `main`, reported by Lead: quant 326 passed / 25 skipped,
.NET unit 1,281, integration 206. **UNVERIFIED** by a file in `tmp/audit-*`.

## GAS P1 fix (sprint 8.3), code only

Merged to `main` in `5f49ad4` (`7301f5c`, `49dd4b7`) and pushed.

| sha | .NET unit | integration | mutants killed |
| --- | --- | --- | --- |
| `7301f5c` | 1,279 | 206 | 3 of 3 |
| `49dd4b7` | 1,281 | 206 | 3 of 3 |

At `7301f5c` the audit probe (`AuditShiftProbeTests`, 9 cases) failed one:
"zero profit, no NCI" — a true zero profit was dropped. `49dd4b7` ("keep a
column that only looks shifted, a non-zero 61 or a zero 62") targets it; that
the probe passes at `49dd4b7` is **UNVERIFIED** (`tmp/audit-49dd4b7/dotnet-test.txt`
has no probe run).

**GAS purge and import: PENDING.** Needs the Moderator's own approval in the
room and the connection-string question settled.

## Open items

- Request builder from the registry: sprint 10 (ADR-034, `e441dcc`).
- `rebalances()` O(n²) lookup: accepted, `ponytail:` comment in `labelled.py`
  (`6087385`).
- GAS purge and import: pending.
- 9.2 branch not yet merged; `CATALOGUE` wiring not yet done.
