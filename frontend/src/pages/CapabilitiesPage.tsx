interface Capability {
  readonly name: string;
  readonly phase: string;
}

interface Tier {
  readonly tier: string;
  readonly name: string;
  readonly summary: string;
  readonly capabilities: readonly Capability[];
}

/**
 * The planned capability map.
 *
 * Every entry below is PLANNED. Nothing here is implemented, and this page
 * exists so that is unambiguous — a terminal that shows empty chart panels
 * implies features that do not exist.
 */
const TIERS: readonly Tier[] = [
  {
    tier: "Tier 1",
    name: "Research Terminal",
    summary: "Know what an instrument is, then what it is doing.",
    capabilities: [
      { name: "Instrument master", phase: "Phase 1" },
      { name: "Historical market data", phase: "Phase 2" },
      { name: "Data quality validation", phase: "Phase 3" },
      { name: "Charts, watchlists, search", phase: "Phase 4" },
      { name: "Fundamentals and news", phase: "Phase 5" },
      { name: "Screener", phase: "Phase 6" },
    ],
  },
  {
    tier: "Tier 2",
    name: "Quant Platform",
    summary: "Turn data into signals, and test whether the signals hold.",
    capabilities: [
      { name: "Factor research", phase: "Phase 7" },
      { name: "Backtesting engine", phase: "Phase 8" },
      { name: "Portfolio engine", phase: "Phase 9" },
      { name: "Risk engine", phase: "Phase 10" },
    ],
  },
  {
    tier: "Tier 3",
    name: "Trading System",
    summary: "Route orders deterministically, and prove the books agree.",
    capabilities: [
      { name: "Paper trading", phase: "Phase 11" },
      { name: "Order management system", phase: "Phase 12" },
      { name: "Broker integration", phase: "Phase 13" },
      { name: "Reconciliation", phase: "Phase 14" },
    ],
  },
  {
    tier: "Tier 4",
    name: "Advanced Engineering",
    summary: "Make the hot path fast, and the research loop assisted.",
    capabilities: [
      { name: "C++ performance engine", phase: "Phase 15" },
      { name: "AI research analyst", phase: "Phase 16" },
      { name: "Production hardening", phase: "Phase 17" },
    ],
  },
];

/** Static roadmap view. Contains no live data by design. */
export function CapabilitiesPage() {
  return (
    <div className="page">
      <div className="page__intro">
        <h1 className="page__title">Capability Map</h1>
        <p className="page__lede">
          The intended shape of the system. Everything on this page is planned —
          the repository is at Phase 0, which builds the foundation only.
        </p>
      </div>

      <div className="tier-grid">
        {TIERS.map((tier) => (
          <section
            key={tier.tier}
            className="panel tier-card"
            aria-labelledby={`tier-${tier.tier.replace(" ", "-")}`}
          >
            <header className="panel__header">
              <div>
                <p className="tier-card__eyebrow numeric">{tier.tier}</p>
                <h2
                  id={`tier-${tier.tier.replace(" ", "-")}`}
                  className="panel__title"
                >
                  {tier.name}
                </h2>
              </div>
            </header>

            <p className="tier-card__summary">{tier.summary}</p>

            <ul className="capability-list">
              {tier.capabilities.map((capability) => (
                <li key={capability.name} className="capability-list__item">
                  <span className="capability-list__name">{capability.name}</span>
                  <span className="capability-list__phase numeric">
                    {capability.phase}
                  </span>
                  <span className="capability-list__badge">PLANNED</span>
                </li>
              ))}
            </ul>
          </section>
        ))}
      </div>
    </div>
  );
}
