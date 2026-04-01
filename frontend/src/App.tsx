import { Route, Routes } from 'react-router-dom';
import { TooltipProvider } from '@/components/ui/tooltip';
import { Toaster } from '@/components/ui/sonner';
import { SidebarInset, SidebarProvider } from '@/components/ui/sidebar';
import { AppSidebar } from '@/components/AppSidebar';
import { AuthGate } from '@/components/AuthGate';
import AppListPage from '@/routes/AppListPage';
import AppDetailPage from '@/routes/AppDetailPage';
import AppCreatePage from '@/routes/AppCreatePage';
import AppTypesListPage from '@/routes/AppTypesListPage';
import AppTypeDetailPage from '@/routes/AppTypeDetailPage';
import AppTypeCreatePage from '@/routes/AppTypeCreatePage';
import CapabilitiesPage from '@/routes/CapabilitiesPage';
import RoutesPage from '@/routes/RoutesPage';
import SystemPage from '@/routes/SystemPage';

export function App() {
  return (
    <AuthGate>
      <TooltipProvider>
        <SidebarProvider>
          <AppSidebar />
          <SidebarInset>
            <Routes>
              <Route path="/" element={<AppListPage />} />
              <Route path="/apps" element={<AppListPage />} />
              <Route path="/apps/new" element={<AppCreatePage />} />
              <Route path="/apps/:id" element={<AppDetailPage />} />
              <Route path="/app-types" element={<AppTypesListPage />} />
              <Route path="/app-types/new" element={<AppTypeCreatePage />} />
              <Route path="/app-types/:id" element={<AppTypeDetailPage />} />
              <Route path="/capabilities" element={<CapabilitiesPage />} />
              <Route path="/routes" element={<RoutesPage />} />
              <Route path="/system" element={<SystemPage />} />
            </Routes>
          </SidebarInset>
        </SidebarProvider>
        <Toaster />
      </TooltipProvider>
    </AuthGate>
  );
}
