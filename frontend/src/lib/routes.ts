const ROUTES = {
  dashboard: '/',
  apps: '/apps',
  appDetail: (slug: string) => `/apps/${slug}`,
  appSettings: (slug: string) => `/apps/${slug}/settings`,
  appCreate: '/apps/new',
  routes: '/routes',
  system: '/system',
  users: '/users',
  userCreate: '/users/new',
} as const

export { ROUTES }
