import { Link } from "react-router-dom";

/** Shown for any unrecognised route. */
export function NotFoundPage() {
  return (
    <div className="page">
      <div className="page__intro">
        <h1 className="page__title">No such view</h1>
        <p className="page__lede">
          That route does not exist. The terminal currently has two views.
        </p>
      </div>
      <Link className="button" to="/">
        Back to Infrastructure
      </Link>
    </div>
  );
}
