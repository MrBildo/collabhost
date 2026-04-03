import { useParams } from 'react-router-dom'

function AppDetailPage() {
  const { slug } = useParams<{ slug: string }>()

  return (
    <div>
      <h1 className="wm-section-title mb-6">{slug ?? 'App Detail'}</h1>
      <p className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
        Operations console: identity, stats strip, detail cards, action bar, and log viewer.
      </p>
    </div>
  )
}

export { AppDetailPage }
