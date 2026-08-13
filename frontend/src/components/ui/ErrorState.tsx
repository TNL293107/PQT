interface ErrorStateProps {
  readonly title?: string;
  readonly detail: string;
  readonly onRetry?: () => void;
}

/**
 * Shown when a query fails.
 *
 * States what failed and offers the one action that can help, rather than a
 * bare "something went wrong".
 */
export function ErrorState({
  title = "Cannot reach the API",
  detail,
  onRetry,
}: ErrorStateProps) {
  return (
    <div className="state-block state-block--error" role="alert">
      <p className="state-block__title">{title}</p>
      <p className="state-block__message">{detail}</p>
      <p className="state-block__hint">
        Check that the backend is running and that <code>VITE_API_BASE_URL</code>{" "}
        points at it.
      </p>
      {onRetry ? (
        <button type="button" className="button" onClick={onRetry}>
          Retry
        </button>
      ) : null}
    </div>
  );
}
