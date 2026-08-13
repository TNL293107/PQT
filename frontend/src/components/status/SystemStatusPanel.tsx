import { StatusPill } from "../ui/StatusPill";
import type { SystemHealth } from "../../types/health";

interface SystemStatusPanelProps {
  readonly health: SystemHealth;
}

const TIME_FORMAT = new Intl.DateTimeFormat(undefined, {
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hour12: false,
});

/** The dependency table on the system status page. */
export function SystemStatusPanel({ health }: SystemStatusPanelProps) {
  return (
    <section className="panel" aria-labelledby="system-status-heading">
      <header className="panel__header">
        <h2 id="system-status-heading" className="panel__title">
          System Status
        </h2>
        <span className="panel__meta numeric">
          {TIME_FORMAT.format(health.checkedAt)}
        </span>
      </header>

      <table className="status-table">
        <caption className="visually-hidden">
          Availability of each service the terminal depends on
        </caption>
        <thead>
          <tr>
            <th scope="col">Service</th>
            <th scope="col">State</th>
            <th scope="col">Detail</th>
          </tr>
        </thead>
        <tbody>
          {health.services.map((service) => (
            <tr key={service.id} data-testid={`status-row-${service.id}`}>
              <th scope="row" className="status-table__service">
                {service.label}
              </th>
              <td>
                <StatusPill status={service.status} />
              </td>
              <td className="status-table__detail">
                {service.detail ?? "—"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
