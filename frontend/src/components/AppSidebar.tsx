import { NavLink, useLocation } from 'react-router-dom';
import { Boxes, Globe, Moon, Server, Sun } from 'lucide-react';
import { useTheme } from '@/hooks/useTheme';
import { Button } from '@/components/ui/button';
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
  { label: 'Apps', href: '/', icon: Boxes },
  { label: 'Routes', href: '/routes', icon: Globe },
  { label: 'System', href: '/system', icon: Server },
] as const;

export function AppSidebar() {
  const { theme, toggleTheme } = useTheme();
  const location = useLocation();

  return (
    <Sidebar>
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
                    ? location.pathname === '/' || location.pathname.startsWith('/apps')
                    : location.pathname.startsWith(item.href);

                return (
                  <SidebarMenuItem key={item.href}>
                    <SidebarMenuButton
                      isActive={isActive}
                      tooltip={item.label}
                      render={<NavLink to={item.href} />}
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
          <Button variant="ghost" size="icon" onClick={toggleTheme} aria-label="Toggle theme">
            {theme === 'light' ? <Moon className="h-4 w-4" /> : <Sun className="h-4 w-4" />}
          </Button>
          <span className="text-xs text-muted-foreground">v0.1.0</span>
        </div>
      </SidebarFooter>
    </Sidebar>
  );
}
