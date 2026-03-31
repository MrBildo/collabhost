export type LookupItem = {
  id: string;
  name: string;
  displayName: string;
};

// -- App Types --

export type AppTypeCapabilityResponse = {
  category: string;
  displayName: string;
  defaults: Record<string, unknown>;
};

export type AppTypeListItem = {
  id: string;
  name: string;
  displayName: string;
  description: string | null;
  isBuiltIn: boolean;
  capabilities: Record<string, AppTypeCapabilityResponse>;
};

export type AppTypeDetail = {
  id: string;
  name: string;
  displayName: string;
  description: string | null;
  isBuiltIn: boolean;
  capabilities: Record<string, AppTypeCapabilityResponse>;
};

export type CreateAppTypeRequest = {
  name: string;
  displayName: string;
  description: string | null;
  capabilities: Record<string, Record<string, unknown>> | null;
};

export type CreateAppTypeResponse = {
  externalId: string;
};

export type UpdateAppTypeRequest = {
  displayName: string;
  description: string | null;
  capabilities: Record<string, Record<string, unknown> | null> | null;
};

// -- Capabilities --

export type CapabilityCatalogItem = {
  slug: string;
  displayName: string;
  description: string | null;
  category: string;
};

// -- Process State --

export type ProcessState =
  | 'Running'
  | 'Stopped'
  | 'Crashed'
  | 'Starting'
  | 'Stopping'
  | 'Restarting'
  | 'Unknown';

// -- App Responses (capability-driven) --

export type AppTypeReference = {
  id: string;
  name: string;
  displayName: string;
};

export type ProcessRuntimeState = {
  state: string;
  pid: number | null;
  uptimeSeconds: number | null;
  restartCount: number;
};

export type RouteRuntimeState = {
  state: string;
  domain: string | null;
};

export type RuntimeState = {
  process: ProcessRuntimeState | null;
  route: RouteRuntimeState | null;
};

export type AppCapabilityResponse = {
  category: string;
  displayName: string;
  resolved: Record<string, unknown>;
  defaults: Record<string, unknown>;
  hasOverrides: boolean;
};

export type AppResponse = {
  id: string;
  name: string;
  displayName: string;
  appType: AppTypeReference;
  registeredAt: string;
  runtime: RuntimeState;
  capabilities: Record<string, AppCapabilityResponse>;
};

export type CreateAppRequest = {
  name: string;
  displayName: string;
  appTypeId: string;
  capabilityOverrides: Record<string, Record<string, unknown> | null> | null;
};

export type CreateAppResponse = {
  externalId: string;
};

export type UpdateAppRequest = {
  displayName: string | null;
  capabilityOverrides: Record<string, Record<string, unknown> | null> | null;
};

// -- Logs --

export type LogEntry = {
  timestamp: string;
  stream: 'stdout' | 'stderr';
  content: string;
};

export type LogsResponse = {
  entries: LogEntry[];
  totalBuffered: number;
};

// -- Routes --

export type RouteEntry = {
  appExternalId: string;
  appName: string;
  domain: string;
  target: string;
  proxyMode: string;
  https: boolean;
};

export type RouteListResponse = {
  routes: RouteEntry[];
  baseDomain: string;
};

// -- System --

export type SystemStatus = {
  status: string;
  version: string;
  timestamp: string;
};

// -- Update SSE --

export type UpdateSseStatusEvent = {
  phase: 'stopping' | 'updating' | 'starting' | 'complete' | 'failed';
};

export type UpdateSseLogEvent = {
  stream: 'stdout' | 'stderr';
  line: string;
};

export type UpdateSseResultEvent = {
  success: boolean;
  exitCode: number;
  timedOut: boolean;
};
