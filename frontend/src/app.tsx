import { Layout } from '@/chrome/Layout'
import { AppCreatePage } from '@/pages/AppCreatePage'
import { AppDetailPage } from '@/pages/AppDetailPage'
import { AppListPage } from '@/pages/AppListPage'
import { AppSettingsPage } from '@/pages/AppSettingsPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { RoutesPage } from '@/pages/RoutesPage'
import { SystemPage } from '@/pages/SystemPage'
import { BrowserRouter, Route, Routes } from 'react-router-dom'

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<DashboardPage />} />
          <Route path="apps" element={<AppListPage />} />
          <Route path="apps/new" element={<AppCreatePage />} />
          <Route path="apps/:slug" element={<AppDetailPage />} />
          <Route path="apps/:slug/settings" element={<AppSettingsPage />} />
          <Route path="routes" element={<RoutesPage />} />
          <Route path="system" element={<SystemPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}

export { App }
