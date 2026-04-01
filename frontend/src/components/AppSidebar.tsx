import { NavLink, useLocation } from 'react-router-dom';
import { Blocks, Boxes, Globe, LayoutDashboard, Layers, Server } from 'lucide-react';
import { ThemeToggle } from '@/components/ui/ThemeToggle';
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarSeparator,
} from '@/components/ui/sidebar';

const NAV_ITEMS = [
  { label: 'Dashboard', href: '/', icon: LayoutDashboard },
  { label: 'Apps', href: '/apps', icon: Boxes },
  { label: 'App Types', href: '/app-types', icon: Layers },
  { label: 'Capabilities', href: '/capabilities', icon: Blocks },
  { label: 'Routes', href: '/routes', icon: Globe },
  { label: 'System', href: '/system', icon: Server },
] as const;

export function AppSidebar() {
  const location = useLocation();

  return (
    <Sidebar className="bg-[image:var(--glass-bg)] backdrop-blur-[var(--glass-blur)] border-r border-[var(--glass-border)]">
      <SidebarHeader className="px-4 py-4">
        <div className="flex items-center gap-2">
          <Server className="h-5 w-5 text-primary" />
          <span className="text-lg font-bold" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            Collabhost
          </span>
        </div>
      </SidebarHeader>
      <SidebarSeparator />
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupContent>
            <SidebarMenu>
              {NAV_ITEMS.map((item) => {
                const isActive =
                  item.href === '/'
                    ? location.pathname === '/'
                    : location.pathname.startsWith(item.href);

                return (
                  <SidebarMenuItem key={item.href}>
                    <SidebarMenuButton
                      isActive={isActive}
                      tooltip={item.label}
                      render={<NavLink to={item.href} />}
                      className={
                        isActive
                          ? 'bg-[image:var(--sidebar-active-bg)] border border-[var(--sidebar-active-border)] text-primary'
                          : ''
                      }
                    >
                      <item.icon />
                      <span>{item.label}</span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                );
              })}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>
      <SidebarSeparator />
      <SidebarFooter>
        <div className="flex items-center justify-between px-2">
          <ThemeToggle />
          <span className="text-xs text-muted-foreground">v0.1.0</span>
        </div>
      </SidebarFooter>
    </Sidebar>
  );
}
