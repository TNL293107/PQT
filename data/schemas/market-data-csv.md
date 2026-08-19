# Market data CSV export

The interchange format the file-backed market data source reads. It is a real
provider from the pipeline's point of view: the same contract, the same
validation, the same audit trail as an HTTP source, and no licence needed to
run it.

Configure it by pointing `MarketData__FileProviderDirectory` at the root
directory. With that setting blank, no source is registered at all and every
ingestion run is recorded as skipped — see `.env.example`.

## Layout

```
<root>/
├── 1m/
├── 5m/
├── 15m/
├── 30m/
├── 1h/
└── 1d/
    ├── FPT.csv
    ├── VNM.csv
    └── ...
```

One directory per resolution, one file per ticker. The file name is the
exchange ticker in upper case, which is the same form the instrument master
stores — not a provider-decorated symbol such as `FPT.HM`.

## Columns

A header row is required. Columns are matched **by name**, not by position, so
a reordered export is read correctly rather than silently transposed.

| Column      | Required | Meaning                                      |
| ----------- | -------- | -------------------------------------------- |
| `timestamp` | yes      | The instant the period **opened**             |
| `open`      | yes      | First traded price                            |
| `high`      | yes      | Highest traded price                          |
| `low`       | yes      | Lowest traded price                           |
| `close`     | yes      | Last traded price                             |
| `volume`    | yes      | Units traded — zero is legitimate             |
| `turnover`  | no       | Cash value traded; blank where not reported   |

```csv
timestamp,open,high,low,close,volume,turnover
2026-08-24,27300,27500,27100,27450,1284300,35063985000
2026-08-25,27450,27800,27400,27750,1502100,41433275000
```

## Rules the format depends on

**The timestamp is the opening edge of the period, never the closing one.**
Both conventions exist in the wild and they differ by exactly one interval,
which is the easiest way to shift an entire series by one period and never
notice it.

**Dates are read as UTC.** A date with no time is midnight UTC. For a daily
Vietnamese series that is the trading date, because every venue here is at
UTC+7 and a session therefore lies wholly inside one UTC day. Numbers and
dates are parsed with invariant formatting, so the same file means the same
thing on every machine.

**Every timestamp must sit on a period boundary.** A `5m` file cannot carry a
row at `09:03`. Rows that do not are rejected with a reason rather than
rounded — a misaligned row is a partial bar or a shifted series, and both look
plausible once stored.

**Rows outside the requested range are not an error.** The source serves the
slice a run asked for; nothing asked for the rest.

## What the pipeline does with a malformed file

| Condition                              | Result                                    |
| -------------------------------------- | ----------------------------------------- |
| File missing                            | Run recorded as **failed**, with a reason |
| Required column missing                 | Run recorded as **failed**, naming it     |
| Unparseable number or date              | Run recorded as **failed**, naming the line |
| A row contradicting itself (high < low) | Row **rejected**, run still succeeds      |
| Same period twice in one file           | Second row **rejected**, first kept       |

Nothing is repaired. Clamping a high up to the close would turn a visible
provider fault into a plausible bar that every later phase computes on.

## Licensing

Do not commit vendor exports to this repository. The rules in
[`../README.md`](../README.md) apply: `data/raw/`, `data/cache/` and
`data/external/` are ignored by Git precisely so that a licensed dataset
cannot be added by accident. A synthetic fixture lives in `../fixtures/` for
demonstration.
