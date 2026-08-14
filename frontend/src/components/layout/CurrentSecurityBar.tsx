import { useCurrentSecurity } from "../../context/currentSecurity";

interface CurrentSecurityBarProps {
  readonly onOpenSearch: () => void;
}

/**
 * The strip under the header showing what the terminal is pointed at.
 *
 * Persistent and always visible, because every panel added from here on
 * describes this security, and a screen full of numbers whose subject is
 * ambiguous is worse than no numbers.
 */
export function CurrentSecurityBar({ onOpenSearch }: CurrentSecurityBarProps) {
  const { security, clear } = useCurrentSecurity();

  if (security === null) {
    return (
      <div className="security-bar security-bar--empty">
        <span className="security-bar__label">No security selected</span>
        <button type="button" className="security-bar__action" onClick={onOpenSearch}>
          Search <kbd className="numeric">Ctrl</kbd>
          <kbd className="numeric">K</kbd>
        </button>
      </div>
    );
  }

  return (
    <div className="security-bar" aria-live="polite">
      <span className="security-bar__label">Security</span>
      <span className="security-bar__ticker numeric">{security.ticker}</span>
      <span className="security-bar__name">{security.name}</span>

      <span className="security-bar__facts">
        <span className="numeric">{security.exchange}</span>
        <span className="numeric">{security.currency}</span>
        <span>{security.assetType}</span>
        {security.status === "Listed" ? null : (
          <span className="security-bar__status numeric">
            {security.status.toUpperCase()}
          </span>
        )}
      </span>

      <button type="button" className="security-bar__action" onClick={onOpenSearch}>
        Change <kbd className="numeric">Ctrl</kbd>
        <kbd className="numeric">K</kbd>
      </button>
      <button type="button" className="security-bar__action" onClick={clear}>
        Clear
      </button>
    </div>
  );
}
