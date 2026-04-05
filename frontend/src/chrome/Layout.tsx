import { Outlet } from 'react-router-dom'
import { Topbar } from './Topbar'

function Layout() {
  return (
    <div className="flex flex-col h-screen">
      <Topbar />
      <main className="flex-1 min-h-0 overflow-y-auto px-4 py-6 mx-auto max-w-screen-xl w-full flex flex-col">
        <Outlet />
      </main>
    </div>
  )
}

export { Layout }
