# Evidence: Sprint 9 (Phase 7 foundations, ADR-033 and ADR-034)

Drafted 2026-10-27 by QA + QC from `git log` on `main` (`e441dcc`), the branch
`feature/phase-7-labelled-dataset-code` (`6087385`) and the Tester + Audit gate
outputs in `tmp/audit-<sha>/`. `tmp/` is git-ignored, so key lines are quoted
here. **PENDING** marks what needs work not yet done.

How to read the gates: each `tmp/audit-<sha>/verdict.txt` holds Tester +
Audit's verdict and counts; `negative_control*.txt` lists planted mutants and
whether the suite killed them. Mutant counts below are the verdict file's
(they include mutants added after the first negative-control run).

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
  **PENDING** — needs a real labelled export, which needs Docker and the
  `CATALOGUE` wiring (Lead, 2026-10-27). Not a failure.

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

Gates (Tester + Audit, `tmp/audit-<sha>/verdict.txt`):

| sha | verdict | Python | .NET unit | integration | mutants killed |
| --- | --- | --- | --- | --- | --- |
| `5549e04` | **FAIL** ("test gaps; code correct") | 305 passed, 14 skipped | 1,282 | 206 | Python 6/8 (survived `py_keep_rebalance_exit_past_to`, `py_missing_exit_close_is_zero`); C# 3/4 (survived `cs_culture_sort`) |
| `1b10d7c` | **PASS** ("tests only on top of 5549e04") | 308 passed, 14 skipped | 1,283 | 206 | Python 8/8, C# 4/4 |

`5549e04` hand check: "grid 4/4, labels 4/4, window 4 rebalances 2-3 names".
`CATALOGUE` wiring: `b44e3a3` ("check a labelled dataset's request against the
feature catalogue by default") on the same branch; its gate is **PENDING**.

## 9.3 Factors registered (ADR-033 catalogue v1)

Merged to `main` in `0573690` and pushed (`bf0080d..5f49ad4`). Eight entries
in `CATALOGUE` (`factors/catalogue.py`): `momentum_12_1`, `momentum_6_1`,
`volatility_60`, `liquidity_20`, `size`, `value_ep`, `value_bp`, `quality_roe`.

All verdicts PASS (`tmp/audit-<sha>/verdict.txt`); ruff, format and mypy ok
at each.

| sha | what | Python | mutants killed |
| --- | --- | --- | --- |
| `ba24601` | bar turnover in reader and DuckDB view (now `fda80f4`) | 284 passed, 25 skipped | 3/3 |
| `abd8cad` | as-of fundamentals row through the harness (now `b72cdfe`) | 300 passed, 14 skipped | 6/7 (survived `p1_forward_fills_last_row`) |
| `a72844a` | momentum 6-1, volatility, liquidity, size | 316 passed, 14 skipped | 12/13 (survived `liq_mean_not_median`: "test window had mean = median") |
| `cb3a7e0` | liquidity test fixed | 316 passed, 14 skipped | 13/13 |
| `f2f6d1f` | value_ep, value_bp, quality_roe | 331 passed, 14 skipped | 10/11 (survived `divide_by_nonpositive_close`: "no test with close_raw < 0") |
| `4e73a34` | size absent at close ≤ 0; share-count caveats | 337 passed, 14 skipped | 12/12 |

The 14 skipped are "13 Qlib, 1 momentum_slice"; at `ba24601` the 25 add "11
job_queue without PQT_TEST_DATABASE_URL".

QA + QC review of `f2f6d1f`: 4 minor, 0 blocker; all four closed in
`87417d7` and `4e73a34`.

`p1_forward_fills_last_row`, re-run on `main` (`tmp/audit-main-rerun/forward_fill_mutant.txt`):

> p1_forward_fills_last_row: killed (1 failed, 5 passed in 0.80s)

Suites on `main` `e441dcc`, run 2026-10-27 by Tester + Audit
(`tmp/audit-main-rerun/main-counts.txt`):

> All checks passed!
> 337 passed, 14 skipped
> Passed! - Failed: 0, Passed: 1281 … PersonalQuant.UnitTests.dll
> Passed! - Failed: 0, Passed: 206 … PersonalQuant.IntegrationTests.dll

(Lead's earlier report of 326 passed / 25 skipped was a different run; the file
above is the one this evidence rests on.)

## GAS P1 fix (sprint 8.3), code only

Merged to `main` in `5f49ad4` (`7301f5c`, `49dd4b7`) and pushed.

| sha | verdict | .NET unit | integration | mutants killed |
| --- | --- | --- | --- | --- |
| `7301f5c` | PASS | 1,279 | 206 | 5/7 (survived `drop_61_is_zero`, `drop_62_not_zero`: "no test held either guard") |
| `49dd4b7` | PASS | 1,281 | 206 | 7/7 ("probe still passes") |

The audit probe (`AuditShiftProbeTests` plus `AllZeroColumnProbeTests`),
re-run on `main` (`tmp/audit-main-rerun/shift_probe.txt`): **10 of 10 passed**
with the code as is, including "zero profit, no NCI". With the GAS shift rule
disabled, the two "shifted column at edge values" cases fail, so the probe
does depend on the rule. (An earlier `probe.txt` at `7301f5c` shows "zero
profit, no NCI" failing; its verdict file records the probe as passing. The
`main` re-run supersedes both.)

**Live-DB scan, read-only** (`tmp/audit-gas-scan/`, every statement in
`BEGIN … ROLLBACK`):

- 1,582 reports, 820 of them Circular 200 (`probe-out.txt`).
- `shift_stored.sql` finds exactly 5 GAS forms, 4 columns each, 62/60 between
  96.77% and 98.74%, no other issuer: `GAS-20220429-eafecbfc6049`,
  `GAS-20221028-fe110add5b3f`, `GAS-20230130-cb4708826d28`,
  `GAS-20230428-6245c9318c1e`, `GAS-20240426-ba25b2a787c4`. No false positive.
- For all five, `some_70_stored` and `full_csharp_rule_hit` are `f`, and
  `shift.sql` returns 0 rows (`shift-out.txt`); the stored forms carry no
  line 70, which the full C# rule reads.
- After the import, `shift_stored.sql` is the post-check: the five forms must
  no longer appear.

**GAS purge and import: PENDING.** Needs the Moderator's own approval in the
room and the connection-string question settled.

## Open items

- Request builder from the registry: sprint 10 (ADR-034, `e441dcc`).
- `rebalances()` O(n²) lookup: accepted, `ponytail:` comment in `labelled.py`
  (`6087385`).
- GAS purge and import: pending.
- 9.2 branch (`6087385`, `b44e3a3`) not yet merged; `b44e3a3` gate pending.
- 9.1 window numbers: pending a real labelled export.
