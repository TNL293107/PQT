interface Capability {
  readonly name: string;
  readonly phase: string;
}

interface Milestone {
  readonly label: string;
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
const MILESTONES: readonly Milestone[] = [
  {
    label: "Milestone 1",
    name: "Data Foundation",
    summary: "Know what an instrument is, then get its history right.",
    capabilities: [
      { name: "Instrument master", phase: "Phase 1" },
      { name: "Market data ingestion", phase: "Phase 2" },
      { name: "Data normalization & quality", phase: "Phase 3" },
      { name: "Corporate actions & adjusted data", phase: "Phase 4" },
    ],
  },
  {
    label: "Milestone 2",
    name: "Quant Platform",
    summary: "Turn data into signals, and test whether the signals hold.",
    capabilities: [
      { name: "Market intelligence terminal", phase: "Phase 5" },
      { name: "Fundamental & financial data", phase: "Phase 6" },
      { name: "News & alternative data", phase: "Phase 7" },
      { name: "Quant research framework", phase: "Phase 8" },
      { name: "Backtesting engine", phase: "Phase 9" },
      { name: "Risk engine", phase: "Phase 10" },
    ],
  },
  {
    label: "Milestone 3",
    name: "Trading System",
    summary: "Route orders deterministically, and prove the books agree.",
    capabilities: [
      { name: "Portfolio management", phase: "Phase 11" },
      { name: "Paper trading", phase: "Phase 12" },
      { name: "Order management system", phase: "Phase 13" },
      { name: "Broker integration", phase: "Phase 14" },
      { name: "Reconciliation", phase: "Phase 15" },
    ],
  },
  {
    label: "Milestone 4",
    name: "Advanced Engineering",
    summary: "Make the hot path fast, and the research loop assisted.",
    capabilities: [
      { name: "C++ performance engine", phase: "Phase 16" },
      { name: "AI research analyst", phase: "Phase 17" },
      { name: "Production hardening", phase: "Phase 18" },
      { name: "Portfolio / public demonstration", phase: "Phase 19" },
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
          The intended shape of the system, targeting the Vietnam market.
          Everything on this page is planned — the repository is at Phase 0,
          which builds the foundation only.
        </p>
      </div>

      <div className="tier-grid">
        {MILESTONES.map((milestone) => (
          <section
            key={milestone.label}
            className="panel tier-card"
            aria-labelledby={`milestone-${milestone.label.replace(" ", "-")}`}
          >
            <header className="panel__header">
              <div>
                <p className="tier-card__eyebrow numeric">{milestone.label}</p>
                <h2
                  id={`milestone-${milestone.label.replace(" ", "-")}`}
                  className="panel__title"
                >
                  {milestone.name}
                </h2>
              </div>
            </header>

            <p className="tier-card__summary">{milestone.summary}</p>

            <ul className="capability-list">
              {milestone.capabilities.map((capability) => (
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
