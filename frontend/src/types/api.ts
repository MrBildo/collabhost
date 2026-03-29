export type LookupItem = {
  id: string;
  name: string;
  displayName: string;
};

export type AppListItem = {
  externalId: string;
  name: string;
  displayName: string;
  appTypeName: string;
  port: number | null;
  updateCommand: string | null;
  updateTimeoutSeconds: number | null;
  autoStart: boolean;
};

export type ProcessState =
  | 'Stopped'
  | 'Starting'
  | 'Running'
  | 'Stopping'
  | 'Crashed'
  | 'Restarting'
  | 'Unknown';

export type ProcessStatus = {
  externalId: string;
  appName: string;
  processState: ProcessState;
  pid: number | null;
  startedAt: string | null;
  uptimeSeconds: number | null;
  restartCount: number;
  lastRestartAt: string | null;
};

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

export type SystemStatus = {
  status: string;
  version: string;
  timestamp: string;
};

export type EnvironmentVariable = {
  name: string;
  value: string;
};

export type AppDetail = {
  externalId: string;
  name: string;
  displayName: string;
  appTypeName: string;
  installDirectory: string;
  commandLine: string;
  arguments: string | null;
  workingDirectory: string | null;
  restartPolicyName: string;
  port: number | null;
  healthEndpoint: string | null;
  updateCommand: string | null;
  updateTimeoutSeconds: number | null;
  autoStart: boolean;
  registeredAt: string;
  environmentVariables: EnvironmentVariable[];
};

export type LogEntry = {
  timestamp: string;
  stream: 'stdout' | 'stderr';
  content: string;
};

export type LogsResponse = {
  entries: LogEntry[];
  totalBuffered: number;
};

export type UpdateAppRequest = {
  displayName: string;
  installDirectory: string;
  commandLine: string;
  arguments: string | null;
  workingDirectory: string | null;
  restartPolicyId: string;
  healthEndpoint: string | null;
  updateCommand: string | null;
  updateTimeoutSeconds: number | null;
  autoStart: boolean;
};

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
  processStatus: ProcessStatus | null;
};

export type CreateAppRequest = {
  name: string;
  displayName: string;
  appTypeId: string;
  installDirectory: string;
  commandLine: string;
  arguments: string | null;
  workingDirectory: string | null;
  restartPolicyId: string;
  healthEndpoint: string | null;
  updateCommand: string | null;
  updateTimeoutSeconds: number | null;
  autoStart: boolean;
};

export type CreateAppResponse = {
  externalId: string;
};
