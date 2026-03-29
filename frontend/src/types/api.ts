export type AppListItem = {
  externalId: string;
  name: string;
  displayName: string;
  appTypeName: 'Executable' | 'NpmPackage' | 'StaticSite';
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
