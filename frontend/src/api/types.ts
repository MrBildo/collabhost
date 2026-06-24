/* API response types — the contract between frontend and backend.
 * These mirror the C# response records in v2-backend-architecture.md.
 * All status/enum values are lowercase strings from the backend.
 * The frontend owns display formatting (see lib/format.ts).
 */

// --- Shared ---

type AppStatus = 'running' | 'stopped' | 'crashed' | 'starting' | 'stopping' | 'restarting' | 'backoff' | 'fatal'

type HealthStatus = 'healthy' | 'unhealthy' | 'degraded' | 'unknown'

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

type StaticSiteProbe = {
  hasIndexHtml: boolean
  htmlFileCount: number
  // Capped at 200MB on the backend; renders as "200 MB+" when at cap.
  totalAssetBytes: number
  // True when the directory uses a recognized nested-asset convention
  // (wwwroot/, assets/, _next/, _astro/, _app/, static/).
  hasNestedAssets: boolean
}

type ExecutableProbe = {
  binaryName: string
  binarySizeBytes: number
  // > 1 means multiple .exe candidates were found at the artifact root.
  candidateBinaryCount: number
  // Soft-nudge channel: when true, the binary looks like a self-contained
  // .NET single-file publish. The panel surfaces a hint suggesting the
  // operator re-register as `dotnet-app` to enable health-check,
  // environment-defaults, etc. (Bill ruling #2 on card #220.)
  isManagedDotnet: boolean
}

// Known probe variants — each carries its typed `data` shape (FE-TYPE-01).
type KnownProbeEntry =
  | { type: 'dotnet-runtime'; label: string; data: DotnetRuntimeProbe }
  | { type: 'dotnet-dependencies'; label: string; data: DotnetDependenciesProbe }
  | { type: 'node'; label: string; data: NodeProbe }
  | { type: 'react'; label: string; data: ReactProbe }
  | { type: 'typescript'; label: string; data: TypeScriptProbe }
  | { type: 'static-site'; label: string; data: StaticSiteProbe }
  | { type: 'executable'; label: string; data: ExecutableProbe }

// Forward-compat catch-all for a probe type the backend adds before the FE
// learns it. The `(string & {})` brand keeps this member's `type` distinct from
// the literal members above, so TS does NOT widen the whole union's discriminant
// to `string` — narrowing on `probe.type` still works for the known variants
// (FE-TYPE-01). Its `data` is `unknown`, never `any`: the UnknownProbePanel is
// the only consumer and treats it opaquely.
type UnknownProbeEntry = { type: string & {}; label: string; data: unknown }

type ProbeEntry = KnownProbeEntry | UnknownProbeEntry

type FieldType = 'text' | 'number' | 'boolean' | 'select' | 'directory' | 'keyvalue'

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

// Probe cache lifecycle (Card #337). Distinguishes never-probed from fresh
// from stale from not-applicable -- before #337 all four collapsed to an
// empty Probes array and the frontend could not render distinct empty-state
// copy. Lowercase-hyphen wire form.
type ProbesStatus = 'fresh' | 'stale' | 'never-probed' | 'not-applicable'

// Detail-page tab identifiers. Backend-authoritative (Card #348, D5) -- the
// FE renders only the tabs the backend declares, in the declared order. New
// AppTypes adding new tabs ship the new identifier here.
type DetailTab = 'logs' | 'technology' | 'health' | 'route'

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
  // Card #337: cache-lifecycle state for the probes array. `fresh` + empty
  // probes means "extractor ran and found nothing extractable"; `never-probed`
  // means "the periodic tick hasn't run yet."
  probesStatus: ProbesStatus
  probes: ProbeEntry[]
  resources: AppResources | null
  route: AppRoute | null
  actions: AppActions
  // Card #326 / #322 E1: per-app writable data path (absolute, runtime-derived
  // from COLLABHOST_DATA_PATH, never persisted). The operator points the app's
  // writable config (e.g. a SQLite connection string) at this path so it lands
  // inside the system-scope unit's ReadWritePaths.
  writableDataPath: string
  // Card #348, D5: backend-authoritative list of detail-page tabs to render,
  // in the order they should appear. The FE does NOT derive tab visibility
  // from appType.slug or actions shape. external-route renders
  // ['health', 'route']; managed apps render ['logs', 'technology'] (or
  // ['logs'] for system-service).
  tabs: DetailTab[]
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
  // Card #308: server-authoritative key-validation contract for keyvalue fields.
  // Regex source string (no delimiters/flags) the field's keys must satisfy, plus
  // the operator-facing message shown on failure. null/absent => the env-var default.
  keyPattern?: string | null
  keyPatternMessage?: string | null
  // Card #338: schema-declared effectiveness predicate. The dependent field is
  // visible-but-inert when the sibling parent field does not equal `value`. The
  // BE camelCases enum `value`s at serialization (so e.g. "fileServer" / "manual"
  // align with the camelCased Select option values). Same-section only -- cross-
  // section dependencies are explicitly out of contract.
  dependsOn?: FieldDependency | null
}

type FieldDependency = {
  field: string
  value: string
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

// Response from POST /apps/{slug}/runtime-config-file/import (Card #336).
// Preview only — the operator reviews `imported`/`skipped` and saves via the
// standard settings-save flow. `skipped` lists top-level entries that were
// not flat string->string (nested objects, arrays, nulls, non-string primitives).
type RuntimeConfigFileImportResponse = {
  imported: Record<string, string>
  skipped: string[]
  sourcePath: string
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
  // A buffer-overflow reset: the incoming id jumped past the buffer cap, so the
  // unrecoverable older range was dropped and the buffer cleared. Distinct from
  // 'gap' (a bounded, in-buffer hole) — this marker discloses the larger loss so
  // a bigger drop is louder in the UI, not quieter (FE-SSE-01). `dropped` is the
  // number of log entries the jump skipped.
  | { type: 'reset'; dropped: number }

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
  appTypes: number
}

type DashboardEventsResponse = {
  events: DashboardEvent[]
}

type DashboardEvent = {
  id: string
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
  isBuiltIn: boolean
}

// --- Registration ---

type RegistrationSchema = {
  appType: RegistrationAppType
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
  proxyState: ProxyState
  portalReachable: boolean
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
  isPortal: boolean
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

type ProxyState = 'starting' | 'running' | 'degraded' | 'failed' | 'disabled' | 'stopped'

type ProxyDetail = {
  lastSyncOk: boolean
  lastSyncError: string | null
  lastSyncAt: string | null
  listenAddress: string
}

type SystemStatus = {
  status: string
  version: string
  hostname: string
  uptimeSeconds: number
  timestamp: string
  proxyState: ProxyState
  portalUrl: string
  portalReachable: boolean
  proxyDetail: ProxyDetail | null
}

// --- Filesystem ---

// Free-string literal at the API boundary (Bill ruling #1 on card #220):
// `notApplicable` is intentionally NOT a member of the backend's
// `DiscoveryStrategy` enum -- that enum represents process-discovery
// strategies for runnable apps. `notApplicable` fires for app types that
// don't go through process discovery at all (`static-site`) or for
// `executable` directories with zero candidate binaries.
type SuggestedStrategy = 'dotNetRuntimeConfiguration' | 'dotNetProject' | 'packageJson' | 'manual' | 'notApplicable'

type DetectStrategyResponse = {
  suggestedStrategy: SuggestedStrategy
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
  ProbesStatus,
  DetailTab,
  ProbeEntry,
  KnownProbeEntry,
  UnknownProbeEntry,
  DotnetRuntimeProbe,
  DotnetDependenciesProbe,
  NotableDependency,
  NodeProbe,
  ReactProbe,
  TypeScriptProbe,
  StaticSiteProbe,
  ExecutableProbe,
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
  FieldDependency,
  UpdateSettingsRequest,
  SettingsValidationError,
  RuntimeConfigFileImportResponse,
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
  ProxyState,
  ProxyDetail,
  DetectStrategyResponse,
  SuggestedStrategy,
  FilesystemBrowseResponse,
  DirectoryEntry,
}
