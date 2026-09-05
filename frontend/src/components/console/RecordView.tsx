import type { Instrument } from "../../types/instrument";
import type { Bar, DataQualityIssue, IngestionRun } from "../../types/marketData";
import { FUNCTIONS, VERBS } from "../../lib/console/entry";
import type { ConsoleRecord } from "../../lib/console/records";
import { Sparkline } from "./Sparkline";

/** Renders whatever a command produced. */
export function RecordView({ record }: { readonly record: ConsoleRecord }) {
  switch (record.kind) {
    case "lines":
      return record.lines.length === 0 ? null : (
        <div className="out">
          {record.lines.map((line, index) => (
            <p className="out__line" key={index}>
              {line}
            </p>
          ))}
        </div>
      );

    case "error":
      return (
        <div className="out out--error">
          <p className="out__line">{record.message}</p>
          {record.hint ? <p className="out__hint">{record.hint}</p> : null}
        </div>
      );

    case "help":
      return <HelpView />;

    case "health":
      return (
        <div className="out">
          <table className="grid">
            <tbody>
              {record.health.services.map((service) => (
                <tr key={service.id}>
                  <td className="grid__key">{service.label}</td>
                  <td>
                    <span className={`dot dot--${service.status}`} aria-hidden="true" />
                    {service.status}
                  </td>
                  <td className="grid__num">
                    {service.latencyMs === undefined ? "" : `${service.latencyMs} ms`}
                  </td>
                  <td className="grid__note">{service.detail ?? ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      );

    case "instruments":
      return <InstrumentsView record={record} />;

    case "description":
      return <DescriptionView instrument={record.instrument} />;

    case "series":
      return <SeriesView record={record} />;

    case "quality":
      return <QualityView record={record} />;

    case "ingestion":
      return <IngestionView instrument={record.instrument} runs={record.runs} />;

    default:
      return null;
  }
}

function HelpView() {
  return (
    <div className="out">
      <p className="out__line out__line--dim">
        A line is <span className="lit">&lt;security&gt; &lt;function&gt;</span>, terminal style.
        Options are <span className="lit">--name value</span>, the same grammar the operator CLI reads.
      </p>

      <table className="grid grid--help">
        <tbody>
          {VERBS.map((verb) => (
            <tr key={verb.code}>
              <td className="grid__key lit">{verb.code.toLowerCase()}</td>
              <td className="grid__note">{verb.summary}</td>
            </tr>
          ))}
          {FUNCTIONS.map((fn) => (
            <tr key={fn.code}>
              <td className="grid__key lit">
                &lt;sec&gt; {fn.code}
              </td>
              <td className="grid__note">
                {fn.summary}
                {fn.options.length > 0 ? (
                  <span className="out__opts">
                    {fn.options.map((option) => ` --${option}`).join("")}
                  </span>
                ) : null}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <p className="out__hint">
        Try <span className="lit">FPT GP --limit 40</span>, then{" "}
        <span className="lit">FPT QLTY</span>. Tab completes a mnemonic; ↑ and ↓ walk history.
      </p>
    </div>
  );
}

function InstrumentsView({
  record,
}: {
  readonly record: Extract<ConsoleRecord, { kind: "instruments" }>;
}) {
  if (record.results.length === 0) {
    return (
      <div className="out out--error">
        <p className="out__line">Nothing in the instrument master matches “{record.query}”.</p>
      </div>
    );
  }

  return (
    <div className="out">
      {record.ambiguous ? (
        <p className="out__line out__line--warn">
          “{record.query}” is listed on more than one venue. Ticker uniqueness is per venue, so
          this is normal — name the one you mean.
        </p>
      ) : null}

      <table className="grid">
        <thead>
          <tr>
            <th>TICKER</th>
            <th>NAME</th>
            <th>VENUE</th>
            <th>TYPE</th>
            <th>STATUS</th>
          </tr>
        </thead>
        <tbody>
          {record.results.map((instrument) => (
            <tr key={instrument.instrumentId}>
              <td className="lit">{instrument.ticker}</td>
              <td className="grid__note">{instrument.name}</td>
              <td>{instrument.exchange}</td>
              <td>{instrument.assetType}</td>
              <td>{instrument.status}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function DescriptionView({ instrument }: { readonly instrument: Instrument }) {
  const rows: readonly (readonly [string, string])[] = [
    ["Ticker", instrument.ticker],
    ["Name", instrument.name],
    ["Venue", instrument.exchange],
    ["Asset type", instrument.assetType],
    ["Currency", instrument.currency],
    ["Status", instrument.status],
    ["Identifier", instrument.instrumentId],
  ];

  return (
    <div className="out">
      <table className="grid grid--pairs">
        <tbody>
          {rows.map(([key, value]) => (
            <tr key={key}>
              <td className="grid__key">{key}</td>
              <td className={key === "Identifier" ? "grid__note" : "lit"}>{value}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="out__hint">
        The identifier is the identity. The ticker is what you read — it changes on an exchange
        transfer and can be reassigned after a delisting.
      </p>
    </div>
  );
}

function SeriesView({ record }: { readonly record: Extract<ConsoleRecord, { kind: "series" }> }) {
  const { series, instrument, knownAsOf } = record;

  if (series.bars.length === 0) {
    return (
      <div className="out out--error">
        <p className="out__line">
          No {series.interval} bars are stored for {instrument.ticker}
          {knownAsOf ? ` as of ${knownAsOf}` : ""}.
        </p>
        <p className="out__hint">
          {knownAsOf
            ? "A period first observed after that instant is absent, not filled from today's value — that is what makes the read point-in-time."
            : "Nothing has been ingested for this series yet."}
        </p>
      </div>
    );
  }

  const first = series.bars[0]!;
  const last = series.bars[series.bars.length - 1]!;
  const change = ((last.close - first.close) / first.close) * 100;
  const sources = [...new Set(series.bars.map((bar) => bar.source))];

  return (
    <div className="out">
      <div className="quote">
        <span className="quote__last">{format(last.close)}</span>
        <span className={`quote__chg ${change >= 0 ? "is-up" : "is-down"}`}>
          {change >= 0 ? "+" : ""}
          {change.toFixed(2)}% over {series.bars.length} sessions
        </span>
        <span className="quote__meta">
          {series.interval} · {sources.join(", ")}
        </span>
      </div>

      <Sparkline bars={series.bars} label={`${instrument.ticker} close`} />

      <div className="scale">
        <span>{first.openedAtUtc.slice(0, 10)}</span>
        <span>{last.openedAtUtc.slice(0, 10)}</span>
      </div>

      <p className="out__line out__line--dim">
        {describeAdjustment(series.adjusted, series.adjustedAtSource, series.adjustedBars)}
        {knownAsOf ? ` Point-in-time as of ${knownAsOf}.` : ""}
      </p>

      <details className="fold">
        <summary>Last ten sessions</summary>
        <table className="grid">
          <thead>
            <tr>
              <th>SESSION</th>
              <th className="grid__num">OPEN</th>
              <th className="grid__num">HIGH</th>
              <th className="grid__num">LOW</th>
              <th className="grid__num">CLOSE</th>
              <th className="grid__num">VOLUME</th>
              <th>SOURCE</th>
            </tr>
          </thead>
          <tbody>
            {series.bars.slice(-10).map((bar) => (
              <tr key={bar.openedAtUtc}>
                <td>{bar.openedAtUtc.slice(0, 10)}</td>
                <td className="grid__num">{format(bar.open)}</td>
                <td className="grid__num">{format(bar.high)}</td>
                <td className="grid__num">{format(bar.low)}</td>
                <td className="grid__num lit">{format(bar.close)}</td>
                <td className="grid__num">{bar.volume.toLocaleString("en-US")}</td>
                <td>{bar.source}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </div>
  );
}

/**
 * Says who did the adjusting, which is not the same question as whether the
 * series is adjusted.
 */
function describeAdjustment(adjusted: boolean, atSource: boolean, applied: number): string {
  if (!adjusted) {
    return "Raw — exactly what the market printed, with no factor applied.";
  }

  if (atSource) {
    return "Adjusted at source. This system applied nothing: the feed had already rescaled it, and adjusting again would be wrong by the product of every factor since.";
  }

  return `Adjusted on read — ${applied} bar${applied === 1 ? "" : "s"} rescaled from recorded corporate actions.`;
}

function QualityView({ record }: { readonly record: Extract<ConsoleRecord, { kind: "quality" }> }) {
  const { report, issues } = record;

  return (
    <div className="out">
      <div className="scores">
        <Score label="Overall" value={report.score.overall} />
        <Score
          label="Completeness"
          value={report.score.completeness}
          unmeasured={!report.calendarIsComplete}
        />
        <Score label="Consistency" value={report.score.consistency} />
        <Score label="Validity" value={report.score.validity} />
        <Score label="Source" value={report.score.sourceReliability} />
      </div>

      {report.calendarIsComplete ? null : (
        <p className="out__line out__line--warn">
          Completeness is unmeasured: the venue&apos;s calendar was not transcribed for the whole
          of {report.fromDate} to {report.toDate}, so a real holiday and a missing session cannot
          be told apart. Reported as unknown rather than computed wrongly.
        </p>
      )}

      {/*
        * The window comes first because every figure above it is measured over
        * that window and nothing else. A series holding a decade of history
        * scores zero completeness against a window it does not reach, and the
        * number is right — it is the missing window that makes it read as a
        * claim about the whole series.
        */}
      <p className="out__line out__line--dim">
        {report.fromDate} → {report.toDate}
        {" · "}
        {report.barsStored} bar{report.barsStored === 1 ? "" : "s"} stored
        {report.calendarIsComplete ? ` against ${report.sessionsExpected} expected sessions` : ""}
        {" · "}
        {report.ingestion.runs} ingestion run{report.ingestion.runs === 1 ? "" : "s"},{" "}
        {report.ingestion.failed} failed
      </p>

      {issues.length === 0 ? (
        <p className="out__line">Nothing is open against this series.</p>
      ) : (
        <table className="grid">
          <thead>
            <tr>
              <th>SESSION</th>
              <th>FINDING</th>
              <th>DETAIL</th>
            </tr>
          </thead>
          <tbody>
            {issues.map((issue: DataQualityIssue) => (
              <tr key={issue.issueId}>
                <td className="grid__key">{issue.sessionAtUtc.slice(0, 10)}</td>
                <td className="lit">{issue.kind}</td>
                <td className="grid__note">{issue.detail}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {issues.length > 0 ? (
        <p className="out__hint">
          A finding stays open until something accounts for it. Close one with{" "}
          <span className="lit">pqt quality resolve &lt;id&gt;</span> on the operator CLI — this
          console reads, and does not write.
        </p>
      ) : null}
    </div>
  );
}

function Score({
  label,
  value,
  unmeasured,
}: {
  readonly label: string;
  readonly value: number;
  readonly unmeasured?: boolean;
}) {
  const percent = Math.round(value * 100);

  return (
    <div className="score">
      <span className="score__key">{label}</span>
      <span className={`score__val ${unmeasured ? "is-unmeasured" : band(percent)}`}>
        {unmeasured ? "n/a" : `${percent}%`}
      </span>
    </div>
  );
}

function band(percent: number): string {
  if (percent >= 95) return "is-good";
  if (percent >= 80) return "is-fair";
  return "is-poor";
}

function IngestionView({
  instrument,
  runs,
}: {
  readonly instrument: Instrument;
  readonly runs: readonly IngestionRun[];
}) {
  if (runs.length === 0) {
    return (
      <div className="out">
        <p className="out__line">Nothing has ever been ingested for {instrument.ticker}.</p>
      </div>
    );
  }

  return (
    <div className="out">
      <table className="grid">
        <thead>
          <tr>
            <th>STARTED</th>
            <th>SOURCE</th>
            <th>OUTCOME</th>
            <th className="grid__num">STORED</th>
            <th className="grid__num">REVISED</th>
            <th className="grid__num">REJECTED</th>
            <th>REASON</th>
          </tr>
        </thead>
        <tbody>
          {runs.map((run) => (
            <tr key={run.runId}>
              <td>{run.startedAtUtc.slice(0, 16).replace("T", " ")}</td>
              <td>{run.source}</td>
              <td className={outcomeClass(run.outcome)}>{run.outcome}</td>
              <td className="grid__num">{run.barsStored}</td>
              <td className="grid__num">{run.barsRevised}</td>
              <td className="grid__num">{run.barsRejected}</td>
              <td className="grid__note">{run.failureReason ?? ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="out__hint">
        Every attempt is here, not only the ones that worked — a pipeline that recorded only its
        successes could not explain a gap.
      </p>
    </div>
  );
}

function outcomeClass(outcome: string): string {
  if (outcome === "Succeeded") return "is-good";
  if (outcome === "Failed") return "is-poor";
  return "is-fair";
}

function format(value: number): string {
  return value.toLocaleString("en-US", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  });
}

export type { Bar };
