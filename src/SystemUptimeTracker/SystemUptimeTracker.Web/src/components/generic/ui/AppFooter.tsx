export default function AppFooter() {
  return (
    <footer className="border-top bg-white">
      <div className="container py-3 d-flex flex-column flex-md-row gap-2 align-items-md-center justify-content-between text-secondary small">
        <div className="d-inline-flex align-items-center gap-2">
          <i className="bi bi-activity text-primary" aria-hidden="true" />
          <span>System Uptime Tracker</span>
        </div>
        <div className="d-inline-flex align-items-center gap-2">
          <i className="bi bi-diagram-3" aria-hidden="true" />
          <span>Application starter</span>
        </div>
      </div>
    </footer>
  );
}
