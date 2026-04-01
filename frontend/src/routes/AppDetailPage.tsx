import { useCallback, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { toast } from 'sonner';
import {
  ArrowLeft,
  ExternalLink,
  Loader2,
  Pencil,
  Play,
  RefreshCw,
  Skull,
  Square,
  Trash2,
  Upload,
} from 'lucide-react';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';
import { GradientButton } from '@/components/ui/GradientButton';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { TypeBadge } from '@/components/ui/TypeBadge';
import { CapabilityPanel } from '@/components/capabilities/CapabilityPanel';
import { CapabilityForm } from '@/components/capabilities/CapabilityForm';
import { LogViewer } from '@/components/LogViewer';
import { useStartApp, useStopApp, useRestartApp, useKillApp } from '@/hooks/useApps';
import { useAppDetail, useAppLogs, useUpdateApp, useDeleteApp } from '@/hooks/useAppDetail';
import { useAppUpdate } from '@/hooks/useAppUpdate';
import type { UpdateEvent } from '@/hooks/useAppUpdate';
import { formatDateTime, formatUptime } from '@/lib/format';
import { cn, toCapabilityEntries } from '@/lib/utils';
import type { AppResponse, ProcessState, UpdateAppRequest } from '@/types/api';

function deriveDisplayStatus(app: AppResponse): ProcessState {
  const processState = app.runtime.process?.state;
  if (processState) {
    return processState as ProcessState;
  }

  if (app.runtime.route?.state === 'active') {
    return 'Running';
  }

  return 'Stopped';
}

export default function AppDetailPage() {
  const { id } = useParams<{ id: string }>();

  if (!id) {
    return (
      <div className="p-6">
        <p className="text-destructive">No app ID provided.</p>
      </div>
    );
  }

  return <AppDetailContent appId={id} />;
}

type AppDetailContentProps = {
  appId: string;
};

function AppDetailContent({ appId }: AppDetailContentProps) {
  const { data: app, isLoading, error } = useAppDetail(appId);
  const startApp = useStartApp();
  const stopApp = useStopApp();
  const restartApp = useRestartApp();
  const killApp = useKillApp();
  const appUpdate = useAppUpdate(appId);
  const [isUpdateDialogOpen, setIsUpdateDialogOpen] = useState(false);

  const hasProcess = app?.runtime.process != null;
  const status: ProcessState = app ? deriveDisplayStatus(app) : 'Stopped';
  const isProcessRunning = status === 'Running';
  const isTransitioning = status === 'Starting' || status === 'Stopping' || status === 'Restarting';
  const isMutating =
    startApp.isPending || stopApp.isPending || restartApp.isPending || killApp.isPending;
  const isDisabled = isTransitioning || isMutating;
  const isUpdateRunning = appUpdate.phase === 'connecting' || appUpdate.phase === 'running';

  const hasUpdateCommand =
    app?.capabilities['process']?.resolved?.['updateCommand'] != null &&
    String(app.capabilities['process'].resolved['updateCommand']).trim() !== '';

  function handleUpdate() {
    setIsUpdateDialogOpen(true);
    appUpdate.start();
  }

  function handleCloseUpdateDialog() {
    setIsUpdateDialogOpen(false);
    if (!isUpdateRunning) {
      appUpdate.reset();
    }
  }

  if (isLoading) {
    return <DetailSkeleton />;
  }

  if (error || !app) {
    return (
      <div className="p-6">
        <Link
          to="/apps"
          className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4" />
          Apps
        </Link>
        <div className="mt-4 rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm text-destructive">
          Failed to load app details. The app may have been deleted or the API is unavailable.
        </div>
      </div>
    );
  }

  const domain = app.runtime.route?.domain ?? null;
  const isRouteActive = app.runtime.route?.state === 'active';

  return (
    <div className="flex h-full flex-col p-6">
      {/* Header */}
      <div className="mb-6 space-y-4">
        <Link
          to="/apps"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4" />
          Apps
        </Link>

        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-center gap-3">
            <h1
              className="text-2xl font-bold tracking-tight"
              style={{ fontFamily: "'Space Grotesk', sans-serif" }}
            >
              {app.displayName}
            </h1>
            <TypeBadge typeName={app.appType.displayName} />
            <StatusBadge status={status} />
            {domain && (
              <a
                href={`https://${domain}`}
                target="_blank"
                rel="noopener noreferrer"
                className={cn(
                  'inline-flex items-center gap-1 text-sm',
                  isRouteActive
                    ? 'text-primary hover:underline'
                    : 'text-muted-foreground line-through',
                )}
              >
                {domain}
                <ExternalLink className="h-3 w-3" />
              </a>
            )}
          </div>

          <div className="flex items-center gap-2">
            {isTransitioning || isMutating ? (
              <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
            ) : (
              <>
                {/* Start — universal, shown when not running */}
                {(status === 'Stopped' || status === 'Crashed') && (
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <GradientButton
                          variant="outline"
                          size="sm"
                          onClick={() =>
                            startApp.mutate(appId, {
                              onSuccess: () => toast.success('App started'),
                              onError: () => toast.error('Failed to start app'),
                            })
                          }
                          disabled={isDisabled}
                        />
                      }
                    >
                      <Play className="mr-1 h-4 w-4" />
                      Start
                    </TooltipTrigger>
                    <TooltipContent>Start app</TooltipContent>
                  </Tooltip>
                )}

                {/* Stop — universal, shown when running */}
                {isProcessRunning && (
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <GradientButton
                          variant="outline"
                          size="sm"
                          onClick={() =>
                            stopApp.mutate(appId, {
                              onSuccess: () => toast.success('App stopped'),
                              onError: () => toast.error('Failed to stop app'),
                            })
                          }
                          disabled={isDisabled}
                        />
                      }
                    >
                      <Square className="mr-1 h-4 w-4" />
                      Stop
                    </TooltipTrigger>
                    <TooltipContent>Stop app</TooltipContent>
                  </Tooltip>
                )}

                {/* Restart — only if process capability AND running */}
                {hasProcess && isProcessRunning && (
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <GradientButton
                          variant="outline"
                          size="sm"
                          onClick={() =>
                            restartApp.mutate(appId, {
                              onSuccess: () => toast.success('App restarted'),
                              onError: () => toast.error('Failed to restart app'),
                            })
                          }
                          disabled={isDisabled}
                        />
                      }
                    >
                      <RefreshCw className="mr-1 h-4 w-4" />
                      Restart
                    </TooltipTrigger>
                    <TooltipContent>Restart app</TooltipContent>
                  </Tooltip>
                )}

                {/* Force Kill — only if process capability AND running */}
                {hasProcess && isProcessRunning && (
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <GradientButton
                          variant="destructive"
                          size="sm"
                          onClick={() =>
                            killApp.mutate(appId, {
                              onSuccess: () => toast.success('Process killed'),
                              onError: () => toast.error('Failed to kill process'),
                            })
                          }
                          disabled={isDisabled}
                        />
                      }
                    >
                      <Skull className="mr-1 h-4 w-4" />
                      Kill
                    </TooltipTrigger>
                    <TooltipContent>Force kill process</TooltipContent>
                  </Tooltip>
                )}
              </>
            )}

            {hasUpdateCommand && (
              <GradientButton size="sm" onClick={handleUpdate} disabled={isUpdateRunning}>
                {isUpdateRunning ? (
                  <Loader2 className="mr-1 h-4 w-4 animate-spin" />
                ) : (
                  <Upload className="mr-1 h-4 w-4" />
                )}
                Update
              </GradientButton>
            )}
          </div>
        </div>
      </div>

      {/* Tabs */}
      <Tabs defaultValue="overview" className="flex min-h-0 flex-1 flex-col">
        <TabsList>
          <TabsTrigger value="overview">Overview</TabsTrigger>
          <TabsTrigger value="configuration">Configuration</TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="flex min-h-0 flex-1 flex-col pt-4">
          <OverviewTab appId={appId} app={app} status={status} />
        </TabsContent>

        <TabsContent value="configuration" className="pt-4">
          <ConfigurationTab appId={appId} app={app} />
        </TabsContent>
      </Tabs>

      {/* Update dialog */}
      {hasUpdateCommand && (
        <Dialog open={isUpdateDialogOpen} onOpenChange={handleCloseUpdateDialog}>
          <DialogContent className="sm:max-w-2xl" showCloseButton={!isUpdateRunning}>
            <DialogHeader>
              <DialogTitle>Update: {app.displayName}</DialogTitle>
              <DialogDescription>Running update command</DialogDescription>
            </DialogHeader>

            <UpdateStreamView phase={appUpdate.phase} events={appUpdate.events} />

            <DialogFooter>
              {!isUpdateRunning && (
                <DialogClose render={<GradientButton variant="outline" />}>Close</DialogClose>
              )}
            </DialogFooter>
          </DialogContent>
        </Dialog>
      )}
    </div>
  );
}

// -- Overview Tab --

type OverviewTabProps = {
  appId: string;
  app: AppResponse;
  status: ProcessState;
};

function OverviewTab({ appId, app, status }: OverviewTabProps) {
  const {
    data: logs,
    isLoading: isLogsLoading,
    refetch: refetchLogs,
    isFetching,
  } = useAppLogs(appId);

  const capabilities = toCapabilityEntries(app.capabilities);

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4">
      {/* Runtime stats */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {app.runtime.process != null && (
          <>
            <StatCard label="PID" value={app.runtime.process.pid?.toString() ?? '--'} />
            <StatCard
              label="Uptime"
              value={
                status === 'Running' && app.runtime.process.uptimeSeconds != null
                  ? formatUptime(app.runtime.process.uptimeSeconds)
                  : '--'
              }
            />
            <StatCard label="Restarts" value={app.runtime.process.restartCount.toString()} />
          </>
        )}

        {app.runtime.route != null && (
          <GlassCard size="sm" className="flex items-center justify-center">
            <GlassCardContent className="py-0 text-center">
              <p className="text-xs text-muted-foreground">Domain</p>
              {app.runtime.route.domain ? (
                <a
                  href={`https://${app.runtime.route.domain}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className={cn(
                    'inline-flex items-center gap-1 text-sm font-medium',
                    app.runtime.route.state === 'active'
                      ? 'text-primary hover:underline'
                      : 'text-muted-foreground line-through',
                  )}
                >
                  {app.runtime.route.domain}
                  <ExternalLink className="h-3 w-3" />
                </a>
              ) : (
                <span className="text-sm text-muted-foreground">--</span>
              )}
            </GlassCardContent>
          </GlassCard>
        )}

        <StatCard label="Registered" value={formatDateTime(app.registeredAt)} />
      </div>

      {/* Capability panel (display mode) */}
      {capabilities.length > 0 && (
        <GlassCard>
          <GlassCardContent>
            <CapabilityPanel capabilities={capabilities} />
          </GlassCardContent>
        </GlassCard>
      )}

      {/* Log viewer */}
      {isLogsLoading ? (
        <div className="flex flex-1 items-center justify-center">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : (
        <LogViewer
          entries={logs?.entries ?? []}
          totalBuffered={logs?.totalBuffered ?? 0}
          isRefreshing={isFetching}
          onRefresh={() => refetchLogs()}
          className="min-h-[300px] flex-1"
        />
      )}
    </div>
  );
}

type StatCardProps = {
  label: string;
  value: string;
};

function StatCard({ label, value }: StatCardProps) {
  return (
    <GlassCard size="sm" className="p-3">
      <GlassCardContent className="p-0 text-center">
        <p className="text-xs text-muted-foreground">{label}</p>
        <p className="font-mono text-sm font-medium">{value}</p>
      </GlassCardContent>
    </GlassCard>
  );
}

// -- Configuration Tab --

type ConfigurationTabProps = {
  appId: string;
  app: AppResponse;
};

function ConfigurationTab({ appId, app }: ConfigurationTabProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [overrides, setOverrides] = useState<Record<string, Record<string, unknown> | null>>({});
  const [editDisplayName, setEditDisplayName] = useState(app.displayName);
  const updateApp = useUpdateApp(appId);
  const deleteApp = useDeleteApp();

  const capabilities = toCapabilityEntries(app.capabilities);

  const handleEdit = useCallback(() => {
    setEditDisplayName(app.displayName);
    setOverrides({});
    setIsEditing(true);
  }, [app.displayName]);

  function handleCancel() {
    setOverrides({});
    setEditDisplayName(app.displayName);
    setIsEditing(false);
  }

  function handleSave() {
    const request: UpdateAppRequest = {
      displayName: editDisplayName !== app.displayName ? editDisplayName : null,
      capabilityOverrides: Object.keys(overrides).length > 0 ? overrides : null,
    };

    updateApp.mutate(request, {
      onSuccess: () => {
        setIsEditing(false);
        setOverrides({});
        toast.success('Configuration saved');
      },
      onError: () => toast.error('Failed to save configuration'),
    });
  }

  return (
    <div className="space-y-6">
      {/* Actions */}
      <div className="flex items-center justify-end gap-2">
        {isEditing ? (
          <>
            <GradientButton variant="outline" size="sm" onClick={handleCancel}>
              Cancel
            </GradientButton>
            <GradientButton size="sm" onClick={handleSave} disabled={updateApp.isPending}>
              {updateApp.isPending && <Loader2 className="mr-1 h-4 w-4 animate-spin" />}
              Save
            </GradientButton>
          </>
        ) : (
          <GradientButton variant="outline" size="sm" onClick={handleEdit}>
            <Pencil className="mr-1 h-4 w-4" />
            Edit
          </GradientButton>
        )}
      </div>

      {updateApp.isError && (
        <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-3 text-sm text-destructive">
          Failed to save changes. {updateApp.error.message}
        </div>
      )}

      {/* Basic info */}
      <GlassCard>
        <GlassCardHeader>
          <GlassCardTitle>Details</GlassCardTitle>
        </GlassCardHeader>
        <GlassCardContent className="divide-y divide-[var(--glass-border)]">
          <div className="grid grid-cols-3 gap-4 py-3">
            <span className="text-sm font-medium text-muted-foreground">Name (slug)</span>
            <span className="col-span-2 font-mono text-sm">{app.name}</span>
          </div>
          <div className="grid grid-cols-3 gap-4 py-3">
            <span className="text-sm font-medium text-muted-foreground">Display Name</span>
            <div className="col-span-2">
              {isEditing ? (
                <input
                  type="text"
                  value={editDisplayName}
                  onChange={(event) => setEditDisplayName(event.target.value)}
                  className="h-8 w-full rounded-md border border-input bg-background px-3 text-sm"
                />
              ) : (
                <span className="text-sm">{app.displayName}</span>
              )}
            </div>
          </div>
          <div className="grid grid-cols-3 gap-4 py-3">
            <span className="text-sm font-medium text-muted-foreground">Type</span>
            <span className="col-span-2 text-sm">{app.appType.displayName}</span>
          </div>
          <div className="grid grid-cols-3 gap-4 py-3">
            <span className="text-sm font-medium text-muted-foreground">Registered At</span>
            <span className="col-span-2 font-mono text-sm">{formatDateTime(app.registeredAt)}</span>
          </div>
        </GlassCardContent>
      </GlassCard>

      {/* Capability configuration */}
      {capabilities.length > 0 && (
        <GlassCard>
          <GlassCardHeader>
            <GlassCardTitle>Capabilities</GlassCardTitle>
          </GlassCardHeader>
          <GlassCardContent>
            {isEditing ? (
              <CapabilityForm capabilities={capabilities} onOverridesChange={setOverrides} />
            ) : (
              <CapabilityPanel capabilities={capabilities} />
            )}
          </GlassCardContent>
        </GlassCard>
      )}

      {/* Delete */}
      <div className="border-t pt-6">
        <Dialog>
          <DialogTrigger
            render={
              <GradientButton variant="destructive" size="sm">
                <Trash2 className="mr-1 h-4 w-4" />
                Delete App
              </GradientButton>
            }
          />
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Delete App</DialogTitle>
              <DialogDescription>
                Are you sure you want to delete <strong>{app.displayName}</strong>? This action
                cannot be undone.
              </DialogDescription>
            </DialogHeader>
            <DialogFooter>
              <DialogClose render={<GradientButton variant="outline" />}>Cancel</DialogClose>
              <GradientButton
                variant="destructive"
                onClick={() =>
                  deleteApp.mutate(appId, {
                    onSuccess: () => toast.success('App deleted'),
                    onError: () => toast.error('Failed to delete app'),
                  })
                }
                disabled={deleteApp.isPending}
              >
                {deleteApp.isPending && <Loader2 className="mr-1 h-4 w-4 animate-spin" />}
                Delete
              </GradientButton>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>
    </div>
  );
}

// -- Update Stream View --

type UpdateStreamViewProps = {
  phase: string;
  events: UpdateEvent[];
};

function UpdateStreamView({ phase, events }: UpdateStreamViewProps) {
  const logLines = events
    .filter((event): event is UpdateEvent & { type: 'log' } => event.type === 'log')
    .map((event) => event.data);

  const statusEvents = events.filter(
    (event): event is UpdateEvent & { type: 'status' } => event.type === 'status',
  );

  const currentPhase =
    statusEvents.length > 0 ? statusEvents[statusEvents.length - 1].data.phase : null;

  const resultEvent = events.find(
    (event): event is UpdateEvent & { type: 'result' } => event.type === 'result',
  );

  return (
    <div className="space-y-3">
      {/* Phase indicator */}
      <div className="flex items-center gap-2">
        {(phase === 'connecting' || phase === 'running') && (
          <Loader2 className="h-4 w-4 animate-spin text-primary" />
        )}
        <span className="text-sm font-medium">
          {phase === 'connecting' && 'Connecting...'}
          {phase === 'running' && `Phase: ${currentPhase ?? 'starting'}`}
          {phase === 'complete' && 'Update complete'}
          {phase === 'failed' && 'Update failed'}
        </span>
      </div>

      {/* Log output */}
      <div className="max-h-[400px] overflow-auto rounded-lg bg-gray-900 p-3 font-mono text-sm text-gray-100 dark:bg-[hsl(222,25%,6%)]">
        {logLines.length === 0 && phase !== 'idle' ? (
          <span className="text-gray-500">Waiting for output...</span>
        ) : (
          logLines.map((line, index) => (
            <div
              key={index}
              className={cn('leading-5', line.stream === 'stderr' && 'text-red-300')}
            >
              {line.line}
            </div>
          ))
        )}
      </div>

      {/* Result summary */}
      {resultEvent && (
        <div
          className={cn(
            'rounded-lg border p-3 text-sm',
            resultEvent.data.success
              ? 'border-green-500/30 bg-green-500/10 text-green-700 dark:text-green-400'
              : 'border-destructive/30 bg-destructive/10 text-destructive',
          )}
        >
          {resultEvent.data.success
            ? `Update completed successfully (exit code ${resultEvent.data.exitCode}).`
            : resultEvent.data.timedOut
              ? 'Update timed out.'
              : `Update failed with exit code ${resultEvent.data.exitCode}.`}
        </div>
      )}
    </div>
  );
}

// -- Skeleton --

function DetailSkeleton() {
  return (
    <div className="p-6">
      <Skeleton className="mb-4 h-4 w-16" />
      <div className="mb-6 flex items-center gap-3">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-5 w-20" />
      </div>
      <div className="grid grid-cols-5 gap-3">
        {Array.from({ length: 5 }).map((_, index) => (
          <Skeleton key={index} className="h-16 rounded-lg" />
        ))}
      </div>
      <Skeleton className="mt-4 h-[300px] rounded-lg" />
    </div>
  );
}
