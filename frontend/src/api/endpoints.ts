import { encodePathSegment } from '@/lib/path-segment'
import { request } from './client'
import type {
  ActionResult,
  AppDetail,
  AppListItem,
  AppSettings,
  AppTypeListItem,
  CreateAppRequest,
  CreateAppResponse,
  CreateUserRequest,
  DashboardEventsResponse,
  DashboardStats,
  DetectStrategyResponse,
  FilesystemBrowseResponse,
  LogsResponse,
  MeResponse,
  RegistrationSchema,
  RouteListResponse,
  RuntimeConfigFileImportResponse,
  SystemStatus,
  UpdateSettingsRequest,
  User,
  UserCreateResponse,
} from './types'

// --- Apps ---

function getApps(): Promise<AppListItem[]> {
  return request('/apps')
}

function getAppDetail(slug: string): Promise<AppDetail> {
  return request(`/apps/${encodePathSegment(slug)}`)
}

function startApp(slug: string): Promise<ActionResult> {
  return request(`/apps/${encodePathSegment(slug)}/start`, { method: 'POST' })
}

function stopApp(slug: string): Promise<ActionResult> {
  return request(`/apps/${encodePathSegment(slug)}/stop`, { method: 'POST' })
}

function restartApp(slug: string): Promise<ActionResult> {
  return request(`/apps/${encodePathSegment(slug)}/restart`, { method: 'POST' })
}

function killApp(slug: string): Promise<ActionResult> {
  return request(`/apps/${encodePathSegment(slug)}/kill`, { method: 'POST' })
}

function deleteApp(slug: string): Promise<void> {
  return request(`/apps/${encodePathSegment(slug)}`, { method: 'DELETE' })
}

// --- Settings ---

function getAppSettings(slug: string): Promise<AppSettings> {
  return request(`/apps/${encodePathSegment(slug)}/settings`)
}

function updateAppSettings(slug: string, body: UpdateSettingsRequest): Promise<AppSettings> {
  return request(`/apps/${encodePathSegment(slug)}/settings`, {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}

// Preview-only import of the operator-managed runtime-config file.
// The endpoint reads <artifactDir>/config.json (or the configured path), returns
// the flat string->string entries and the names of any skipped non-flat entries.
// It does NOT persist — the operator reviews the preview and saves via the
// standard settings-save flow.
function importRuntimeConfigFile(slug: string): Promise<RuntimeConfigFileImportResponse> {
  return request(`/apps/${encodePathSegment(slug)}/runtime-config-file/import`, { method: 'POST' })
}

// --- Logs ---

function getAppLogs(
  slug: string,
  params?: { lines?: number; stream?: 'all' | 'stdout' | 'stderr' },
): Promise<LogsResponse> {
  const searchParams = new URLSearchParams()
  if (params?.lines) searchParams.set('lines', String(params.lines))
  if (params?.stream) searchParams.set('stream', params.stream)
  const qs = searchParams.toString()
  return request(`/apps/${encodePathSegment(slug)}/logs${qs ? `?${qs}` : ''}`)
}

// --- Dashboard ---

function getDashboardStats(): Promise<DashboardStats> {
  return request('/dashboard/stats')
}

function getDashboardEvents(limit?: number): Promise<DashboardEventsResponse> {
  const qs = limit ? `?limit=${limit}` : ''
  return request(`/dashboard/events${qs}`)
}

// --- App Types ---

function getAppTypes(): Promise<AppTypeListItem[]> {
  return request('/app-types')
}

function getRegistrationSchema(typeSlug: string): Promise<RegistrationSchema> {
  return request(`/app-types/${encodePathSegment(typeSlug)}/registration`)
}

// --- Create App ---

function createApp(body: CreateAppRequest): Promise<CreateAppResponse> {
  return request('/apps', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

// --- Users ---

function fetchUsers(): Promise<User[]> {
  return request('/users')
}

function fetchUser(id: string): Promise<User> {
  return request(`/users/${encodePathSegment(id)}`)
}

function createUser(body: CreateUserRequest): Promise<UserCreateResponse> {
  return request('/users', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

function deactivateUser(id: string): Promise<void> {
  return request(`/users/${encodePathSegment(id)}/deactivate`, { method: 'PATCH' })
}

function fetchMe(): Promise<MeResponse> {
  return request('/auth/me')
}

// --- Routes ---

function getRoutes(): Promise<RouteListResponse> {
  return request('/routes')
}

function reloadProxy(): Promise<void> {
  return request('/proxy/reload', { method: 'POST' })
}

// --- System ---

function getSystemStatus(): Promise<SystemStatus> {
  return request('/status')
}

// --- Filesystem ---

function browseFilesystem(path: string): Promise<FilesystemBrowseResponse> {
  return request(`/filesystem/browse?path=${encodeURIComponent(path)}`)
}

function detectStrategy(path: string, appTypeSlug: string): Promise<DetectStrategyResponse> {
  const params = new URLSearchParams({ path, appTypeSlug })
  return request(`/filesystem/detect-strategy?${params.toString()}`)
}

export {
  fetchUsers,
  fetchUser,
  createUser,
  deactivateUser,
  fetchMe,
  getApps,
  getAppDetail,
  startApp,
  stopApp,
  restartApp,
  killApp,
  deleteApp,
  getAppSettings,
  updateAppSettings,
  importRuntimeConfigFile,
  getAppLogs,
  getDashboardStats,
  getDashboardEvents,
  getAppTypes,
  getRegistrationSchema,
  createApp,
  getRoutes,
  reloadProxy,
  getSystemStatus,
  browseFilesystem,
  detectStrategy,
}
