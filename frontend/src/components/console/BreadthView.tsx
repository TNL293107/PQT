import type { Breadth, BreadthCategory, BreadthMember, UniverseList } from "../../types/universe";
import { change, price } from "../../lib/console/quoteFormat";

/** How many names each mover table shows. */
const MOVERS = 5;

/**
 * The glyph and word for each category.
 *
 * Direction is always written, never only coloured: a reader who cannot tell
 * the green from the red, or is reading a pasted transcript, still reads ▲.
 */
const CATEGORY: Readonly<Record<BreadthCategory, { glyph: string; word: string; tone: string }>> = {
  Advancing: { glyph: "▲", word: "advancing", tone: "is-up" },
  Declining: { glyph: "▼", word: "declining", tone: "is-down" },
  Unchanged: { glyph: "=", word: "unchanged", tone: "is-flat" },
  NoData: { glyph: "·", word: "no data", tone: "is-unmeasured" },
};

/**
 * Draws a breadth count the server made.
 *
 * Nothing here recounts. The totals are the server's, and the rows are shown
 * as it classified them — a client that re-derived "advancing" from the
 * closes would disagree with it on the first ex-date (ADR-026).
 */
export function BreadthView({ breadth }: { readonly breadth: Breadth }) {
  if (breadth.memberCount === 0) {
    return (
      <div className="out">
        <p className="out__line">
          {breadth.universe} had no members on {breadth.session}. That is what its recorded
          membership says for a day it claims to know — not a gap in the data.
        </p>
      </div>
    );
  }

  const measured = breadth.members.filter((member) => member.change !== null);
  const leaders = measured
    .filter((member) => member.category === "Advancing")
    .toSorted((a, b) => (b.change ?? 0) - (a.change ?? 0))
    .slice(0, MOVERS);
  const laggards = measured
    .filter((member) => member.category === "Declining")
    .toSorted((a, b) => (a.change ?? 0) - (b.change ?? 0))
    .slice(0, MOVERS);
  const restated = breadth.members.filter((member) => member.adjustedForAction);
  const missing = breadth.members.filter((member) => member.category === "NoData");

  return (
    <div className="out breadth">
      <div className="quote">
        <span className="quote__last">{breadth.universe}</span>
        <span className="quote__meta">
          {breadth.session}
          {breadth.sessionWasDefaulted ? " · the latest session any member has a bar for" : ""}
        </span>
      </div>

      <Counts breadth={breadth} />
      <AdvanceDeclineBar breadth={breadth} />

      <p className="out__line out__line--dim">
        Volume ▲ {number(breadth.advancingVolume)} · ▼ {number(breadth.decliningVolume)} · ={" "}
        {number(breadth.unchangedVolume)}
        {" · against "}
        {breadth.venues.map((venue) => `${venue.code} ${venue.previousSession}`).join(", ")}
      </p>

      <div className="movers">
        <Movers title="Top movers" members={leaders} empty="Nothing advanced." />
        <Movers title="Bottom movers" members={laggards} empty="Nothing declined." />
      </div>

      {restated.length > 0 ? (
        <p className="out__line out__line--warn">
          {restated
            .map(
              (member) =>
                `${member.ticker} went ex: its previous close ${price(member.rawPreviousClose)} is restated to ${price(member.previousClose)}`,
            )
            .join("; ")}
          . Measured on adjusted prices, so the entitlement is not counted as a decline — a
          broker&apos;s board will show it red.
        </p>
      ) : null}

      {missing.length > 0 ? (
        <p className="out__line out__line--dim">
          No data for {missing.map((member) => member.ticker).join(", ")}: no bar on the session or
          the one before. Counted apart, never as unchanged.
        </p>
      ) : null}

      <details className="fold">
        <summary>All {breadth.memberCount} members</summary>
        <MemberTable members={sortByChange(breadth.members)} />
      </details>
    </div>
  );
}

function Counts({ breadth }: { readonly breadth: Breadth }) {
  const counts: readonly (readonly [BreadthCategory, number])[] = [
    ["Advancing", breadth.advancing],
    ["Declining", breadth.declining],
    ["Unchanged", breadth.unchanged],
    ["NoData", breadth.noData],
  ];

  return (
    <div className="scores">
      {counts.map(([category, count]) => (
        <div className="score" key={category}>
          <span className="score__key">{CATEGORY[category].word.toUpperCase()}</span>
          <span className={`score__val ${CATEGORY[category].tone}`}>
            {CATEGORY[category].glyph} {count}
          </span>
        </div>
      ))}
    </div>
  );
}

/**
 * One bar split by category, widths in proportion to the counts.
 *
 * Each segment carries its glyph as text and the whole bar a label that reads
 * the counts out, so the bar says the same thing to a screen reader and in
 * greyscale as it does in colour. No-data is hatched as well as greyed: it is
 * the segment most worth not mistaking for "unchanged".
 */
function AdvanceDeclineBar({ breadth }: { readonly breadth: Breadth }) {
  const segments: readonly (readonly [BreadthCategory, number, string])[] = [
    ["Advancing", breadth.advancing, "up"],
    ["Unchanged", breadth.unchanged, "flat"],
    ["Declining", breadth.declining, "down"],
    ["NoData", breadth.noData, "none"],
  ];

  const label = `${breadth.advancing} advancing, ${breadth.unchanged} unchanged, ${breadth.declining} declining, ${breadth.noData} without data, of ${breadth.memberCount} members`;

  return (
    <div className="ad-bar" role="img" aria-label={label}>
      {segments
        .filter(([, count]) => count > 0)
        .map(([category, count, modifier]) => (
          <span
            key={category}
            className={`ad-bar__seg ad-bar__seg--${modifier}`}
            style={{ flexGrow: count }}
            title={`${count} ${CATEGORY[category].word}`}
            aria-hidden="true"
          >
            {CATEGORY[category].glyph}
          </span>
        ))}
    </div>
  );
}

function Movers({
  title,
  members,
  empty,
}: {
  readonly title: string;
  readonly members: readonly BreadthMember[];
  readonly empty: string;
}) {
  return (
    <section className="movers__side" aria-label={title}>
      <p className="score__key">{title.toUpperCase()}</p>
      {members.length === 0 ? (
        <p className="out__line out__line--dim">{empty}</p>
      ) : (
        <MemberTable members={members} />
      )}
    </section>
  );
}

function MemberTable({ members }: { readonly members: readonly BreadthMember[] }) {
  return (
    <table className="grid">
      <thead>
        <tr>
          <th>TICKER</th>
          <th className="grid__num">CHANGE</th>
          <th className="grid__num">CLOSE</th>
          <th className="grid__num">PREV</th>
          <th className="grid__num">VOLUME</th>
        </tr>
      </thead>
      <tbody>
        {members.map((member) => (
          <tr key={member.instrumentId}>
            <td className="lit">{member.ticker}</td>
            <td className={`grid__num ${CATEGORY[member.category].tone}`}>
              {CATEGORY[member.category].glyph} {change(member.change)}
            </td>
            <td className="grid__num">{price(member.close)}</td>
            <td className="grid__num">
              {price(member.previousClose ?? member.rawPreviousClose)}
              {member.adjustedForAction ? " *" : ""}
            </td>
            <td className="grid__num">{member.volume === null ? "—" : number(member.volume)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** Lists the universes a deployment defines. */
export function UniversesView({ listing }: { readonly listing: UniverseList }) {
  if (listing.universes.length === 0) {
    return (
      <div className="out">
        <p className="out__line">No universe is defined on this deployment.</p>
      </div>
    );
  }

  return (
    <div className="out">
      <table className="grid">
        <thead>
          <tr>
            <th>CODE</th>
            <th>NAME</th>
            <th>KIND</th>
            <th>COVERAGE</th>
            <th className="grid__num">MEMBERS {listing.asOf}</th>
          </tr>
        </thead>
        <tbody>
          {listing.universes.map((universe) => (
            <tr key={universe.code}>
              <td className="lit">{universe.code}</td>
              <td className="grid__note">{universe.name}</td>
              <td>{universe.kind}</td>
              <td>{coverage(universe.coverageFrom, universe.coverageUntil)}</td>
              <td className={`grid__num ${universe.memberCount === null ? "is-unmeasured" : ""}`}>
                {universe.memberCount === null ? "unknown" : universe.memberCount}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="out__hint">
        A count of <span className="lit">unknown</span> is a universe whose declared coverage does
        not reach today — not an empty one. Try <span className="lit">breadth VN30</span>.
      </p>
    </div>
  );
}

function coverage(from: string | null, until: string | null): string {
  if (from === null) return "none declared";
  return until === null ? `${from} →` : `${from} → before ${until}`;
}

/** Largest change first, members without data last, ties by ticker. */
function sortByChange(members: readonly BreadthMember[]): readonly BreadthMember[] {
  const unmeasured = (member: BreadthMember) => (member.change === null ? 1 : 0);

  return members.toSorted(
    (a, b) =>
      unmeasured(a) - unmeasured(b) ||
      (b.change ?? 0) - (a.change ?? 0) ||
      a.ticker.localeCompare(b.ticker),
  );
}

function number(value: number): string {
  return value.toLocaleString("en-US");
}
