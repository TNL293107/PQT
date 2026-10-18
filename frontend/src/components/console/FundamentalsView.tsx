import type { FundamentalRatios, Ratio } from "../../types/fundamentals";

/** How each ratio is labelled and printed. */
const RATIOS: ReadonlyArray<{ readonly name: string; readonly label: string; readonly format: (value: number) => string }> = [
  { name: "close", label: "Close", format: (value) => grouped(value, 0) },
  { name: "shares", label: "Shares", format: (value) => grouped(value, 0) },
  { name: "netProfitTtm", label: "Net profit (TTM)", format: (value) => `${grouped(value / 1e9, 0)} bn` },
  { name: "epsTtm", label: "EPS (TTM)", format: (value) => grouped(value, 0) },
  { name: "bookValuePerShare", label: "Book value / share", format: (value) => grouped(value, 0) },
  { name: "priceToEarnings", label: "P/E", format: (value) => value.toFixed(2) },
  { name: "priceToBook", label: "P/B", format: (value) => value.toFixed(2) },
  { name: "returnOnEquity", label: "ROE (TTM)", format: (value) => `${(value * 100).toFixed(2)}%` },
  { name: "dividendYield", label: "Dividend yield (TTM)", format: (value) => `${(value * 100).toFixed(2)}%` },
];

/**
 * Renders an issuer's point-in-time ratios (ADR-030).
 *
 * Every value and every reason for an absence comes from the server. A ratio
 * it could not compute shows its reason in place of a number — never a zero,
 * which would claim the issuer earned nothing.
 */
export function FundamentalsView({ ratios }: { readonly ratios: FundamentalRatios }) {
  const byName = new Map(ratios.ratios.map((ratio) => [ratio.name, ratio] as const));

  return (
    <div className="out">
      <p className="out__line">
        Session <span className="lit">{ratios.session}</span> · statements known before it opened
        {ratios.scope ? ` · ${ratios.scope.toLowerCase()}` : ""}
      </p>
      <p className="out__line out__line--dim">
        Balance sheet {ratios.balanceSheet ?? "none known"} · profit{" "}
        {ratios.profitQuarters.length > 0 ? ratios.profitQuarters.join(" ") : "none"} · actions{" "}
        {ratios.actionsApplied} applied, {ratios.actionsWithheld} withheld
      </p>

      <table className="grid">
        <thead>
          <tr>
            <th>RATIO</th>
            <th>VALUE</th>
            <th>ABSENT BECAUSE</th>
          </tr>
        </thead>
        <tbody>
          {RATIOS.map(({ name, label, format }) => {
            const ratio: Ratio | undefined = byName.get(name);

            return (
              <tr key={name} data-absent={ratio?.value == null ? "true" : undefined}>
                <td className="grid__key">{label}</td>
                <td className="grid__num">{ratio?.value == null ? "—" : format(ratio.value)}</td>
                <td className="grid__note">{ratio?.absent ?? ""}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function grouped(value: number, digits: number): string {
  return value.toLocaleString("en-US", { maximumFractionDigits: digits, minimumFractionDigits: digits });
}
