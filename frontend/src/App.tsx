import { Route, Routes } from 'react-router-dom';
import { TooltipProvider } from '@/components/ui/tooltip';
import { SidebarInset, SidebarProvider } from '@/components/ui/sidebar';
import { AppSidebar } from '@/components/AppSidebar';
import { AuthGate } from '@/components/AuthGate';
import { AppListPage } from '@/routes/AppListPage';
import { AppDetailPage } from '@/routes/AppDetailPage';
import { RoutesPage } from '@/routes/RoutesPage';
import { SystemPage } from '@/routes/SystemPage';

export function App() {
  return (
    <AuthGate>
      <TooltipProvider>
        <SidebarProvider>
          <AppSidebar />
          <SidebarInset>
            <Routes>
              <Route path="/" element={<AppListPage />} />
              <Route path="/apps/:id" element={<AppDetailPage />} />
              <Route path="/routes" element={<RoutesPage />} />
              <Route path="/system" element={<SystemPage />} />
            </Routes>
          </SidebarInset>
        </SidebarProvider>
      </TooltipProvider>
    </AuthGate>
  );
}
