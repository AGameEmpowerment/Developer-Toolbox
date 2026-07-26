import type { ReactNode } from "react";

import Link from "next/link";

type ProductRouteStubProps = {
  title: string;
  route: string;
  summary: string;
  nextStory: string;
  children?: ReactNode;
  primaryHref?: string;
  primaryLabel?: string;
  secondaryHref?: string;
  secondaryLabel?: string;
};

export default function ProductRouteStub({
  title,
  route,
  summary,
  nextStory,
  children,
  primaryHref = "/",
  primaryLabel = "Back to home",
  secondaryHref,
  secondaryLabel,
}: ProductRouteStubProps) {
  return (
    <main className="container py-4 py-md-5">
      <section className="rounded-4 border bg-white shadow-sm p-4 p-lg-5">
        <div className="d-inline-flex align-items-center gap-2 small text-uppercase text-secondary fw-semibold mb-3">
          <i className="bi bi-signpost-split text-primary" aria-hidden="true" />
          <span>Product route stub</span>
        </div>

        <h1 className="display-6 fw-semibold mb-3">{title}</h1>

        <p className="lead text-secondary mb-3">{summary}</p>

        <div className="alert alert-light border" role="status">
          <div className="fw-semibold">Reserved route</div>
          <div>
            <code>{route}</code> is present so later backlog work can land on
            the intended URL without changing the shell structure.
          </div>
        </div>

        {children ? <div className="mb-4">{children}</div> : null}

        <div className="small text-secondary mb-4">
          Planned follow-on story:{" "}
          <span className="fw-semibold">{nextStory}</span>
        </div>

        <div className="d-flex flex-wrap gap-2">
          <Link href={primaryHref} className="btn btn-outline-secondary">
            {primaryLabel}
          </Link>
          {secondaryHref && secondaryLabel ? (
            <Link href={secondaryHref} className="btn btn-primary">
              {secondaryLabel}
            </Link>
          ) : null}
        </div>
      </section>
    </main>
  );
}
