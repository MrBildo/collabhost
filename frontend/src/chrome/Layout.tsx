import { Outlet } from 'react-router-dom'
import { Topbar } from './Topbar'

function Layout() {
  return (
    <div className="flex flex-col min-h-screen">
      <Topbar />
      <main className="flex-1 px-4 py-6 mx-auto max-w-screen-xl w-full">
        <Outlet />
      </main>
    </div>
  )
}

export { Layout }
