import { cn } from '@/lib/cn'
import { ROUTES } from '@/lib/routes'
import { StatusDot } from '@/status/StatusDot'
import { NavLink } from 'react-router-dom'

type NavItem = {
  label: string
  to: string
}

const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', to: ROUTES.dashboard },
  { label: 'Apps', to: ROUTES.apps },
  { label: 'Routes', to: ROUTES.routes },
  { label: 'System', to: ROUTES.system },
]

function Topbar() {
  return (
    <nav className="wm-topbar" aria-label="Main navigation">
      <div className="flex items-center h-full px-4 mx-auto max-w-screen-xl w-full">
        {/* Brand */}
        <NavLink to={ROUTES.dashboard} className="wm-topbar__brand flex items-center gap-2 mr-6">
          <StatusDot status="running" />
          Collabhost
        </NavLink>

        {/* Nav links */}
        <div className="flex items-center h-full">
          {NAV_ITEMS.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              className={({ isActive }) =>
                cn('wm-topbar__link flex items-center px-3.5', isActive && 'wm-topbar__link--active')
              }
            >
              {item.label}
            </NavLink>
          ))}
        </div>

        {/* Right side: hostname + shortcut hint */}
        <div className="ml-auto flex items-center gap-4">
          <span className="text-xs" style={{ color: 'var(--wm-text-dim)', letterSpacing: '0.04em' }}>
            <kbd
              className="px-1.5 py-0.5 rounded"
              style={{
                background: 'var(--wm-bg-inset)',
                border: '1px solid var(--wm-border)',
                fontSize: '9px',
                fontFamily: 'var(--wm-mono)',
              }}
            >
              Ctrl+K
            </kbd>
          </span>
        </div>
      </div>
    </nav>
  )
}

export { Topbar }
