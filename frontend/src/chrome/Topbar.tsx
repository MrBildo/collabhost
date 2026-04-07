import { UserIndicator } from '@/chrome/UserIndicator'
import { useCurrentUser } from '@/hooks/use-current-user'
import { cn } from '@/lib/cn'
import { ROUTES } from '@/lib/routes'
import { StatusDot } from '@/status/StatusDot'
import { NavLink } from 'react-router-dom'

type NavItem = {
  label: string
  to: string
}

const BASE_NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', to: ROUTES.dashboard },
  { label: 'Apps', to: ROUTES.apps },
  { label: 'Routes', to: ROUTES.routes },
  { label: 'System', to: ROUTES.system },
]

function Topbar() {
  const { data: currentUser } = useCurrentUser()
  const isAdmin = currentUser?.role === 'administrator'

  const navItems = isAdmin ? [...BASE_NAV_ITEMS, { label: 'Users', to: ROUTES.users }] : BASE_NAV_ITEMS

  return (
    <nav className="wm-topbar" aria-label="Main navigation">
      <div className="flex items-center h-full px-4 mx-auto max-w-screen-xl w-full">
        {/* Brand */}
        <NavLink to={ROUTES.dashboard} className="wm-topbar__brand flex items-center gap-2">
          <StatusDot status="running" />
          Collabhost
        </NavLink>

        {/* Nav links */}
        <div className="flex items-center h-full">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              className={({ isActive }) =>
                cn('wm-topbar__link flex items-center', isActive && 'wm-topbar__link--active')
              }
            >
              {item.label}
            </NavLink>
          ))}
        </div>

        {/* Right side: user identity */}
        <div className="ml-auto flex items-center gap-4">
          <UserIndicator />
        </div>
      </div>
    </nav>
  )
}

export { Topbar }
