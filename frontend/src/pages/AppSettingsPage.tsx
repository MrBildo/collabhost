import { useParams } from 'react-router-dom'

function AppSettingsPage() {
  const { slug } = useParams<{ slug: string }>()

  return (
    <div>
      <h1 className="wm-section-title mb-6">{slug ?? 'App'} / Settings</h1>
      <p className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
        Schema-driven settings with sections, fields, badges, and danger zone.
      </p>
    </div>
  )
}

export { AppSettingsPage }
