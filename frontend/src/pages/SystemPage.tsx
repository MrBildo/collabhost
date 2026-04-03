function SystemPage() {
  return (
    <div>
      <h1 className="wm-section-title mb-6">System</h1>
      <p className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
        System status, health checks, hostname, and version information.
      </p>
    </div>
  )
}

export { SystemPage }
