/* API response types — the contract between frontend and backend.
 * These mirror the C# response records in v2-backend-architecture.md.
 * All status/enum values are lowercase strings from the backend.
 * The frontend owns display formatting (see lib/format.ts).
 */

// --- Shared ---

type AppStatus = 'running' | 'stopped' | 'crashed' | 'starting' | 'stopping' | 'restarting' | 'backoff' | 'fatal'

type HealthStatus = 'healthy' | 'unhealthy' | 'degraded' | 'unknown'

type AppTag = {
  label: string
  group: 'runtime' | 'framework' | 'tooling'
}

// --- Probes ---

type DotnetRuntimeProbe = {
  tfm: string
  runtimeVersion: string
  isAspNetCore: boolean
  isSelfContained: boolean
  serverGc: boolean
}

type NotableDependency = {
  name: string
  version: string | null
}

type DotnetDependenciesProbe = {
  packageCount: number
  projectReferenceCount: number
  notable: NotableDependency[]
}

type NodeProbe = {
  engineVersion: string | null
  packageManager: string | null
  packageManagerVersion: string | null
  moduleSystem: 'esm' | 'commonjs' | null
  dependencyCount: number
  devDependencyCount: number
}

type ReactProbe = {
  version: string
  bundler: string | null
  bundlerVersion: string | null
  router: string | null
  stateManagement: string | null
  cssStrategy: string | null
}

type TypeScriptProbe = {
  version: string | null
  strict: boolean
  target: string | null
  module: string | null
}

type ProbeEntry =
  | { type: 'dotnet-runtime'; label: string; data: DotnetRuntimeProbe }
  | { type: 'dotnet-dependencies'; label: string; data: DotnetDependenciesProbe }
  | { type: 'node'; label: string; data: NodeProbe }
  | { type: 'react'; label: string; data: ReactProbe }
  | { type: 'typescript'; label: string; data: TypeScriptProbe }
  | { type: string; label: string; data: Record<string, unknown> }

type FieldType = 'text' | 'number' | 'boolean' | 'select' | 'directory' | 'keyValue' | 'keyvalue'

type FieldEditable = { mode: 'always' } | { mode: 'locked'; reason: string } | { mode: 'derived'; reason: string }

type FieldOption = {
  value: string
  label: string
}

// --- App List ---

type AppListItem = {
  id: string
  name: string
  displayName: string
  appType: AppTypeRef
  status: AppStatus
  domain: string | null
  domainActive: boolean
  port: number | null
  uptimeSeconds: number | null
  actions: AppListActions
}

type AppTypeRef = {
  slug: string
  displayName: string
}

type AppListActions = {
  canStart: boolean
  canStop: boolean
}

// --- App Detail ---

type AppDetail = {
  id: string
  name: string
  displayName: string
  appType: AppTypeDetailRef
  registeredAt: string
  status: AppStatus
  pid: number | null
  port: number | null
  uptimeSeconds: number | null
  restartCount: number
  restartPolicy: string | null
  autoStart: boolean | null
  domain: string | null
  domainActive: boolean
  healthStatus: HealthStatus | null
  probes: ProbeEntry[]
  resources: AppResources | null
  route: AppRoute | null
  actions: AppActions
}

type AppTypeDetailRef = {
  slug: string
  displayName: string
}

type AppResources = {
  cpuPercent: number | null
  memoryMb: number | null
  handleCount: number | null
}

type AppRoute = {
  domain: string
  target: string
  tls: boolean
}

type AppActions = {
  canStart: boolean
  canStop: boolean
  canRestart: boolean
  canKill: boolean
  canUpdate: boolean
}

// --- App Settings ---

type AppSettings = {
  id: string
  name: string
  displayName: string
  appTypeName: string
  registeredAt: string
  sections: SettingsSection[]
}

type SettingsSection = {
  key: string
  title: string
  fields: SettingsField[]
}

type SettingsField = {
  key: string
  label: string
  type: FieldType
  value: unknown
  defaultValue: unknown
  editable: FieldEditable
  requiresRestart?: boolean
  options?: FieldOption[]
  helpText?: string
  unit?: string
}

type UpdateSettingsRequest = {
  changes: Record<string, Record<string, unknown>>
}

type SettingsValidationError = {
  errors: Array<{
    section: string
    field: string
    message: string
  }>
}

// --- Action Result ---

type ActionResult = {
  id: string
  status: AppStatus
  actions: AppActions
}

// --- Logs ---

type LogsResponse = {
  entries: LogEntry[]
  totalBuffered: number
}

type LogEntry = {
  id: number
  timestamp: string
  stream: 'stdout' | 'stderr'
  content: string
  level?: string
}

// --- SSE Log Stream ---

type StreamEntry =
  | { type: 'log'; entry: LogEntry }
  | { type: 'status'; state: AppStatus; timestamp: string }
  | { type: 'gap' }

// --- Dashboard ---

type DashboardStats = {
  totalApps: number
  running: number
  stopped: number
  crashed: number
  backoff: number
  fatal: number
  issues: number
  issuesSummary: string | null
  uptimePercent24h: number | null
  incidentsThisWeek: number
  memoryUsedMb: number | null
  memoryTotalMb: number | null
  requestsPerMinute: number | null
  appTypes: number
}

type DashboardEventsResponse = {
  events: DashboardEvent[]
}

type DashboardEvent = {
  timestamp: string
  message: string
  appSlug: string | null
  source: string
  severity: 'info' | 'warning' | 'error'
}

// --- App Types ---

type AppTypeListItem = {
  slug: string
  displayName: string
  description: string | null
  tags: AppTag[]
  isBuiltIn: boolean
}

// --- Registration ---

type RegistrationSchema = {
  appType: RegistrationAppType
  tags: AppTag[]
  sections: RegistrationSection[]
}

type RegistrationAppType = {
  slug: string
  displayName: string
  description: string | null
}

type RegistrationSection = {
  key: string
  title: string
  fields: RegistrationField[]
}

type RegistrationField = {
  key: string
  label: string
  type: FieldType
  required: boolean
  defaultValue: unknown
  placeholder?: string
  helpText?: string
  options?: FieldOption[]
}

type CreateAppRequest = {
  name: string
  displayName: string
  appTypeSlug: string
  values: Record<string, Record<string, unknown>>
}

type CreateAppResponse = {
  id: string
}

// --- Routes ---

type RouteListResponse = {
  routes: RouteEntry[]
  baseDomain: string
}

type RouteEntry = {
  appExternalId: string
  appName: string
  appDisplayName: string
  domain: string
  target: string
  proxyMode: string
  https: boolean
  enabled: boolean
}

// --- Users ---

type UserRole = 'administrator' | 'agent'

type User = {
  id: string
  name: string
  role: UserRole
  isActive: boolean
  createdAt: string
}

type UserCreateResponse = {
  id: string
  name: string
  role: UserRole
  isActive: boolean
  createdAt: string
  authKey: string
}

type MeResponse = {
  id: string
  name: string
  role: UserRole
  isActive: boolean
  createdAt: string
}

type CreateUserRequest = {
  name: string
  role: UserRole
}

// --- System ---

type SystemStatus = {
  status: string
  version: string
  hostname: string
  uptimeSeconds: number
  timestamp: string
}

// --- Filesystem ---

type DetectStrategyResponse = {
  suggestedStrategy: string
  evidence: string[]
}

type FilesystemBrowseResponse = {
  currentPath: string
  parent: string | null
  directories: DirectoryEntry[]
}

type DirectoryEntry = {
  name: string
  path: string
}

export type {
  UserRole,
  User,
  UserCreateResponse,
  MeResponse,
  CreateUserRequest,
  AppStatus,
  HealthStatus,
  AppTag,
  ProbeEntry,
  DotnetRuntimeProbe,
  DotnetDependenciesProbe,
  NotableDependency,
  NodeProbe,
  ReactProbe,
  TypeScriptProbe,
  FieldType,
  FieldEditable,
  FieldOption,
  AppListItem,
  AppTypeRef,
  AppListActions,
  AppDetail,
  AppTypeDetailRef,
  AppResources,
  AppRoute,
  AppActions,
  AppSettings,
  SettingsSection,
  SettingsField,
  UpdateSettingsRequest,
  SettingsValidationError,
  ActionResult,
  LogsResponse,
  LogEntry,
  StreamEntry,
  DashboardStats,
  DashboardEventsResponse,
  DashboardEvent,
  AppTypeListItem,
  RegistrationSchema,
  RegistrationAppType,
  RegistrationSection,
  RegistrationField,
  CreateAppRequest,
  CreateAppResponse,
  RouteListResponse,
  RouteEntry,
  SystemStatus,
  DetectStrategyResponse,
  FilesystemBrowseResponse,
  DirectoryEntry,
}
