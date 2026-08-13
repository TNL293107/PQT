interface LoadingStateProps {
  readonly message?: string;
}

/** Placeholder shown while a query is in flight. */
export function LoadingState({ message = "Querying system status" }: LoadingStateProps) {
  return (
    <div className="state-block" role="status" aria-live="polite">
      <div className="state-block__scanner" aria-hidden="true" />
      <p className="state-block__message">{message}</p>
    </div>
  );
}
