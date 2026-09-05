import { Console } from "../components/console/Console";

/**
 * The terminal's primary surface.
 *
 * A console rather than a dashboard of panels, because a command language is
 * what this system already has: the operator CLI drives the same application
 * layer with the same grammar, and giving the two surfaces one shape means
 * what somebody learns here transfers straight to the shell.
 */
export function ConsolePage() {
  return <Console />;
}
