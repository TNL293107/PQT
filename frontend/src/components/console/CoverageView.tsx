import type {
  CorporateActionCoverage,
  CoverageState,
  FundamentalsCoverage,
  InstrumentCoverage,
  PriceCoverage,
  UniverseCoverage,
} from "../../types/coverage";

/**
 * How each state is drawn: a mark and a word, never a colour alone.
 *
 * The mark is decoration and is hidden from assistive technology; the word is
 * what a screen reader announces and what survives a monochrome display. The
 * pair for `NotMeasured` shares nothing with the pair for `NotHeld` — that is
 * the distinction ADR-026 exists to keep.
 */
const STATES: Readonly<Record<CoverageState, { readonly mark: string; readonly label: string }>> = {
  Held: { mark: "✓", label: "held" },
  NotHeld: { mark: "✗", label: "not held" },
  NotMeasured: { mark: "?", label: "not measured" },
  NotCollected: { mark: "—", label: "not collected" },
};

/** One line of the coverage table, before it is drawn. */
interface Row {
  readonly key: string;
  readonly dataset: string;
  readonly state: CoverageState;
  readonly extent: string;
  readonly detail: string;
  readonly note: string | null;
}

/** Renders what PQT holds for an instrument, dataset by dataset. */
export function CoverageView({ coverage }: { readonly coverage: InstrumentCoverage }) {
  const rows: Row[] = [
    ...coverage.prices.map(priceRow),
    actionRow(coverage.corporateActions),
    ...coverage.universes.map(universeRow),
    fundamentalsRow(coverage.fundamentals),
  ];

  return (
    <div className="out">
      <p className="out__line">
        <span className="lit">{coverage.ticker}</span> {coverage.name} · {coverage.exchange}
      </p>

      <table className="grid coverage">
        <thead>
          <tr>
            <th>DATASET</th>
            <th>STATE</th>
            <th>EXTENT</th>
            <th>DETAIL</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key} data-state={row.state}>
              <td className="grid__key">{row.dataset}</td>
              <td>
                <StateMark state={row.state} />
              </td>
              <td>{row.extent}</td>
              <td className="grid__note">
                {row.detail}
                {row.note ? (
                  <span className="coverage__note">
                    {row.detail ? <br /> : null}
                    {row.note}
                  </span>
                ) : null}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {coverage.universes.length === 0 ? (
        <p className="out__line out__line--dim">
          No universe is defined, so membership cannot be stated either way.
        </p>
      ) : null}

      <p className="out__hint">
        <StateMark state="Held" /> · <StateMark state="NotHeld" /> — known absent ·{" "}
        <StateMark state="NotMeasured" /> — PQT cannot say · <StateMark state="NotCollected" /> —
        not collected yet. Not measured is never the same as not held.
      </p>
    </div>
  );
}

/** A state as its mark and its word. */
export function StateMark({ state }: { readonly state: CoverageState }) {
  // A state this build does not know — one added on the server later — is
  // shown by its own name rather than taking the view down.
  const { mark, label } = (STATES as Partial<Record<string, { mark: string; label: string }>>)[state] ?? {
    mark: "·",
    label: state,
  };

  return (
    <span className={`cov-state cov-state--${state}`}>
      <span className="cov-state__mark" aria-hidden="true">
        {mark}
      </span>
      {label}
    </span>
  );
}

function priceRow(price: PriceCoverage): Row {
  const facts: string[] = [];

  if (price.state === "Held") {
    facts.push(`${count(price.bars)} bar${price.bars === 1 ? "" : "s"}`);
    facts.push(price.sources.join(", "));
    // When the count is absent the server's note says why; repeating it here
    // would word the same fact twice, and the wording is the server's.
    if (price.missingSessions !== null && price.sessionsExpected !== null) {
      facts.push(`${count(price.missingSessions)} of ${count(price.sessionsExpected)} sessions missing`);
    }
  }

  if (price.openFindings > 0) {
    facts.push(`${price.openFindings} open finding${price.openFindings === 1 ? "" : "s"}`);
  }

  if (price.lastAttempt) {
    const { startedAtUtc, source, outcome } = price.lastAttempt;
    facts.push(`last attempt ${startedAtUtc.slice(0, 10)} ${source} ${outcome}`);
  }

  return {
    key: `price-${price.interval}`,
    dataset: `Price ${price.intervalCode}`,
    state: price.state,
    extent: span(price.firstSession, price.lastSession),
    detail: facts.join(" · "),
    note: price.note,
  };
}

function actionRow(actions: CorporateActionCoverage): Row {
  const facts: string[] = [];

  if (actions.state === "Held") {
    facts.push(`${count(actions.count)} action${actions.count === 1 ? "" : "s"}`);

    if (actions.withoutAnnouncementDate > 0) {
      facts.push(`${count(actions.withoutAnnouncementDate)} without announcement date`);
    }

    if (actions.cancelled > 0) {
      facts.push(`${count(actions.cancelled)} cancelled`);
    }
  }

  return {
    key: "corporate-actions",
    dataset: "Corporate actions",
    state: actions.state,
    extent: span(actions.firstExDate, actions.lastExDate),
    detail: facts.join(" · "),
    note: actions.note,
  };
}

function fundamentalsRow(fundamentals: FundamentalsCoverage): Row {
  const held = fundamentals.state === "Held";
  const detail = held
    ? [
        `${count(fundamentals.reports)} report${fundamentals.reports === 1 ? "" : "s"}`,
        fundamentals.lastDisclosedOn ? `last disclosed ${fundamentals.lastDisclosedOn}` : "",
        fundamentals.sources.join(", "),
      ]
        .filter(Boolean)
        .join(" · ")
    : (fundamentals.note ?? "");

  return {
    key: "fundamentals",
    dataset: "Fundamentals",
    state: fundamentals.state,
    extent:
      fundamentals.firstPeriod && fundamentals.lastPeriod
        ? `${fundamentals.firstPeriod} → ${fundamentals.lastPeriod}`
        : "",
    detail,
    note: held ? fundamentals.note : null,
  };
}

function universeRow(universe: UniverseCoverage): Row {
  const first = universe.spells[0];
  const last = universe.spells[universe.spells.length - 1];

  return {
    key: `universe-${universe.code}`,
    dataset: `Universe ${universe.code}`,
    state: universe.state,
    extent: first && last ? `${first.from} → ${last.to ?? "now"}` : "",
    detail:
      universe.spells.length === 0
        ? ""
        : `${universe.spells.length} spell${universe.spells.length === 1 ? "" : "s"}`,
    note: universe.note,
  };
}

function span(first: string | null, last: string | null): string {
  return first !== null && last !== null ? `${first} → ${last}` : "";
}

function count(value: number): string {
  return value.toLocaleString("en-US");
}
