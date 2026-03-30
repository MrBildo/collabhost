import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { toast } from 'sonner';
import {
  ArrowLeft,
  ExternalLink,
  Loader2,
  Pencil,
  Play,
  Plus,
  RefreshCw,
  Square,
  Trash2,
  Upload,
  X,
} from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
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
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { LogViewer } from '@/components/LogViewer';
import { useAppStatus, useStartApp, useStopApp, useRestartApp } from '@/hooks/useApps';
import { useAppDetail, useAppLogs, useUpdateAppConfig, useDeleteApp } from '@/hooks/useAppDetail';
import { useAppUpdate } from '@/hooks/useAppUpdate';
import type { UpdateEvent } from '@/hooks/useAppUpdate';
import { useRestartPolicies } from '@/hooks/useLookups';
import { APP_TYPE_NAMES, BASE_DOMAIN, STATUS_MAP } from '@/lib/constants';
import { formatDateTime, formatUptime } from '@/lib/format';
import { cn } from '@/lib/utils';
import type { AppDetail, ProcessState, UpdateAppRequest } from '@/types/api';

export function AppDetailPage() {
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
  const { data: status } = useAppStatus(appId);
  const startApp = useStartApp();
  const stopApp = useStopApp();
  const restartApp = useRestartApp();
  const appUpdate = useAppUpdate(appId);
  const [isUpdateDialogOpen, setIsUpdateDialogOpen] = useState(false);

  const isStaticSite = app?.appTypeName === APP_TYPE_NAMES.STATIC_SITE;
  const isProtected = app?.isProtected ?? false;
  const processState: ProcessState = status?.processState ?? 'Stopped';
  const statusConfig = STATUS_MAP[processState];
  const isTransitioning =
    processState === 'Starting' || processState === 'Stopping' || processState === 'Restarting';
  const isMutating = startApp.isPending || stopApp.isPending || restartApp.isPending;
  const isDisabled = isTransitioning || isMutating;
  const isUpdateRunning = appUpdate.phase === 'connecting' || appUpdate.phase === 'running';

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
          to="/"
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

  return (
    <div className="flex h-full flex-col p-6">
      {/* Header */}
      <div className="mb-6 space-y-4">
        <Link
          to="/"
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
            <Badge variant="secondary">{app.appTypeName}</Badge>
          </div>

          <div className="flex items-center gap-2">
            {/* Status indicator */}
            {isStaticSite ? (
              <span className="mr-2 text-sm text-muted-foreground">Served by proxy</span>
            ) : (
              <div className="flex items-center gap-2 mr-2">
                <div className={cn('h-2 w-2 rounded-full', statusConfig.color)} />
                <span className="text-sm">{statusConfig.label}</span>
                {status?.pid !== null && status?.pid !== undefined && (
                  <span className="text-xs text-muted-foreground">PID {status.pid}</span>
                )}
              </div>
            )}

            {/* Action buttons */}
            {isStaticSite ? null : isTransitioning || isMutating ? (
              <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
            ) : (
              <>
                {(processState === 'Stopped' || processState === 'Crashed') && (
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() =>
                            startApp.mutate(appId, {
                              onSuccess: () => toast.success('App started'),
                              onError: () => toast.error('Failed to start app'),
                            })
                          }
                          disabled={isDisabled}
                        >
                          <Play className="mr-1 h-4 w-4" />
                          Start
                        </Button>
                      }
                    />
                    <TooltipContent>Start app</TooltipContent>
                  </Tooltip>
                )}

                {processState === 'Running' && (
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() =>
                            stopApp.mutate(appId, {
                              onSuccess: () => toast.success('App stopped'),
                              onError: () => toast.error('Failed to stop app'),
                            })
                          }
                          disabled={isDisabled}
                        >
                          <Square className="mr-1 h-4 w-4" />
                          Stop
                        </Button>
                      }
                    />
                    <TooltipContent>Stop app</TooltipContent>
                  </Tooltip>
                )}

                {(processState === 'Running' || processState === 'Crashed') && (
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() =>
                            restartApp.mutate(appId, {
                              onSuccess: () => toast.success('App restarted'),
                              onError: () => toast.error('Failed to restart app'),
                            })
                          }
                          disabled={isDisabled}
                        >
                          <RefreshCw className="mr-1 h-4 w-4" />
                          Restart
                        </Button>
                      }
                    />
                    <TooltipContent>Restart app</TooltipContent>
                  </Tooltip>
                )}
              </>
            )}

            {app.updateCommand && (
              <Button size="sm" onClick={handleUpdate} disabled={isUpdateRunning}>
                {isUpdateRunning ? (
                  <Loader2 className="mr-1 h-4 w-4 animate-spin" />
                ) : (
                  <Upload className="mr-1 h-4 w-4" />
                )}
                Update
              </Button>
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
          <OverviewTab appId={appId} app={app} processState={processState} />
        </TabsContent>

        <TabsContent value="configuration" className="pt-4">
          <ConfigurationTab appId={appId} app={app} isProtected={isProtected} />
        </TabsContent>
      </Tabs>

      {/* Update dialog */}
      <Dialog open={isUpdateDialogOpen} onOpenChange={handleCloseUpdateDialog}>
        <DialogContent className="sm:max-w-2xl" showCloseButton={!isUpdateRunning}>
          <DialogHeader>
            <DialogTitle>Update: {app.displayName}</DialogTitle>
            <DialogDescription>
              Running update command:{' '}
              <code className="rounded bg-muted px-1 py-0.5 font-mono text-xs">
                {app.updateCommand}
              </code>
            </DialogDescription>
          </DialogHeader>

          <UpdateStreamView phase={appUpdate.phase} events={appUpdate.events} />

          <DialogFooter>
            {!isUpdateRunning && (
              <DialogClose render={<Button variant="outline" />}>Close</DialogClose>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

// -- Overview Tab --

type OverviewTabProps = {
  appId: string;
  app: AppDetail;
  processState: ProcessState;
};

function OverviewTab({ appId, app, processState }: OverviewTabProps) {
  const { data: status } = useAppStatus(appId);
  const {
    data: logs,
    isLoading: isLogsLoading,
    refetch: refetchLogs,
    isFetching,
  } = useAppLogs(appId);

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4">
      {/* Stats row */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        <StatCard label="PID" value={status?.pid?.toString() ?? '--'} />
        <StatCard
          label="Uptime"
          value={
            processState === 'Running' && status?.uptimeSeconds != null
              ? formatUptime(status.uptimeSeconds)
              : '--'
          }
        />
        <StatCard label="Restarts" value={status?.restartCount?.toString() ?? '0'} />
        <StatCard label="Port" value={app.port?.toString() ?? '--'} />
        {app.isRoutable && (
          <Card className="flex items-center justify-center p-3">
            <div className="text-center">
              <p className="text-xs text-muted-foreground">Domain</p>
              <a
                href={`https://${app.name}.${BASE_DOMAIN}`}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline"
              >
                {app.name}.{BASE_DOMAIN}
                <ExternalLink className="h-3 w-3" />
              </a>
            </div>
          </Card>
        )}
      </div>

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
    <Card className="p-3">
      <CardContent className="p-0 text-center">
        <p className="text-xs text-muted-foreground">{label}</p>
        <p className="font-mono text-sm font-medium">{value}</p>
      </CardContent>
    </Card>
  );
}

// -- Configuration Tab --

type ConfigurationTabProps = {
  appId: string;
  app: AppDetail;
  isProtected: boolean;
};

function ConfigurationTab({ appId, app, isProtected }: ConfigurationTabProps) {
  const [isEditing, setIsEditing] = useState(false);
  const updateConfig = useUpdateAppConfig(appId);
  const deleteApp = useDeleteApp();
  const { data: restartPolicies } = useRestartPolicies();
  const isStaticSite = app.appTypeName === APP_TYPE_NAMES.STATIC_SITE;

  const [form, setForm] = useState(() => buildFormState(app));

  function handleEdit() {
    setForm(buildFormState(app));
    setIsEditing(true);
  }

  function handleCancel() {
    setForm(buildFormState(app));
    setIsEditing(false);
  }

  function handleSave() {
    const policyGuid =
      restartPolicies?.find((p) => p.displayName === form.restartPolicyId)?.id ?? '';

    const request: UpdateAppRequest = {
      displayName: form.displayName,
      installDirectory: form.installDirectory,
      commandLine: form.commandLine,
      arguments: form.arguments || null,
      workingDirectory: form.workingDirectory || null,
      restartPolicyId: policyGuid,
      healthEndpoint: form.healthEndpoint || null,
      updateCommand: form.updateCommand || null,
      updateTimeoutSeconds: form.updateTimeoutSeconds ? Number(form.updateTimeoutSeconds) : null,
      autoStart: form.autoStart,
    };

    updateConfig.mutate(request, {
      onSuccess: () => {
        setIsEditing(false);
        toast.success('Configuration saved');
      },
      onError: () => toast.error('Failed to save configuration'),
    });
  }

  function updateField(field: keyof ConfigFormState, value: string | boolean) {
    setForm((previous) => ({ ...previous, [field]: value }));
  }

  function handleAddEnvVar() {
    setForm((previous) => ({
      ...previous,
      environmentVariables: [...previous.environmentVariables, { name: '', value: '' }],
    }));
  }

  function handleRemoveEnvVar(index: number) {
    setForm((previous) => ({
      ...previous,
      environmentVariables: previous.environmentVariables.filter((_, i) => i !== index),
    }));
  }

  function handleEnvVarChange(index: number, field: 'name' | 'value', value: string) {
    setForm((previous) => ({
      ...previous,
      environmentVariables: previous.environmentVariables.map((envVar, i) =>
        i === index ? { ...envVar, [field]: value } : envVar,
      ),
    }));
  }

  const fields: ConfigField[] = [
    { label: 'Name (slug)', value: app.name, readOnly: true },
    { label: 'Display Name', value: form.displayName, field: 'displayName' },
    { label: 'Type', value: app.appTypeName, readOnly: true },
    { label: 'Install Directory', value: form.installDirectory, field: 'installDirectory' },
    { label: 'Command', value: form.commandLine, field: 'commandLine' },
    { label: 'Arguments', value: form.arguments, field: 'arguments' },
    { label: 'Working Directory', value: form.workingDirectory, field: 'workingDirectory' },
    { label: 'Port', value: app.port?.toString() ?? '--', readOnly: true },
    { label: 'Health Endpoint', value: form.healthEndpoint, field: 'healthEndpoint' },
    { label: 'Update Command', value: form.updateCommand, field: 'updateCommand' },
    {
      label: 'Update Timeout (seconds)',
      value: form.updateTimeoutSeconds,
      field: 'updateTimeoutSeconds',
    },
    ...(isStaticSite
      ? []
      : [
          {
            label: 'Restart Policy',
            value: form.restartPolicyId,
            field: 'restartPolicyId' as const,
            isSelect: true,
            selectOptions: restartPolicies ?? [],
          },
          {
            label: 'Auto Start',
            value: form.autoStart ? 'Yes' : 'No',
            field: 'autoStart' as const,
            isBoolean: true,
            booleanValue: form.autoStart,
          },
        ]),
    { label: 'Registered At', value: formatDateTime(app.registeredAt), readOnly: true },
  ];

  return (
    <div className="space-y-6">
      {/* Actions */}
      <div className="flex items-center justify-end gap-2">
        {isEditing ? (
          <>
            <Button variant="outline" size="sm" onClick={handleCancel}>
              Cancel
            </Button>
            <Button size="sm" onClick={handleSave} disabled={updateConfig.isPending}>
              {updateConfig.isPending && <Loader2 className="mr-1 h-4 w-4 animate-spin" />}
              Save
            </Button>
          </>
        ) : (
          <Button variant="outline" size="sm" onClick={handleEdit}>
            <Pencil className="mr-1 h-4 w-4" />
            Edit
          </Button>
        )}
      </div>

      {updateConfig.isError && (
        <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-3 text-sm text-destructive">
          Failed to save changes. {updateConfig.error.message}
        </div>
      )}

      {/* Config fields */}
      <Card>
        <CardContent className="divide-y p-0">
          {fields.map((field) => (
            <div key={field.label} className="grid grid-cols-3 gap-4 px-4 py-3">
              <span className="text-sm font-medium text-muted-foreground">{field.label}</span>
              <div className="col-span-2">
                {isEditing && !field.readOnly ? (
                  field.isBoolean ? (
                    <label className="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={field.booleanValue ?? false}
                        onChange={(event) => updateField(field.field!, event.target.checked)}
                        className="rounded border-input"
                      />
                      <span className="text-sm">{field.booleanValue ? 'Yes' : 'No'}</span>
                    </label>
                  ) : field.isSelect && field.selectOptions ? (
                    <Select
                      value={form[field.field!] as string}
                      onValueChange={(value) => {
                        if (value !== null) {
                          updateField(field.field!, value);
                        }
                      }}
                    >
                      <SelectTrigger className="h-8 w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {field.selectOptions.map((option) => (
                          <SelectItem key={option.id} value={option.displayName}>
                            {option.displayName}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  ) : (
                    <Input
                      value={field.value}
                      onChange={(event) => updateField(field.field!, event.target.value)}
                      className="h-8"
                    />
                  )
                ) : (
                  <span className="text-sm font-mono break-all">
                    {field.readOnly ? field.value : field.value || '--'}
                  </span>
                )}
              </div>
            </div>
          ))}
        </CardContent>
      </Card>

      {/* Environment Variables */}
      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-medium">Environment Variables</h3>
          {isEditing && (
            <Button variant="outline" size="xs" onClick={handleAddEnvVar}>
              <Plus className="mr-1 h-3 w-3" />
              Add
            </Button>
          )}
        </div>

        {form.environmentVariables.length === 0 ? (
          <p className="text-sm text-muted-foreground">No environment variables configured.</p>
        ) : (
          <Card>
            <CardContent className="divide-y p-0">
              {form.environmentVariables.map((envVar, index) => (
                <div key={index} className="flex items-center gap-3 px-4 py-2">
                  {isEditing ? (
                    <>
                      <Input
                        value={envVar.name}
                        onChange={(event) => handleEnvVarChange(index, 'name', event.target.value)}
                        placeholder="Name"
                        className="h-8 flex-1 font-mono"
                      />
                      <span className="text-muted-foreground">=</span>
                      <Input
                        value={envVar.value}
                        onChange={(event) => handleEnvVarChange(index, 'value', event.target.value)}
                        placeholder="Value"
                        className="h-8 flex-1 font-mono"
                      />
                      <Button
                        variant="ghost"
                        size="icon-xs"
                        onClick={() => handleRemoveEnvVar(index)}
                      >
                        <X className="h-3 w-3" />
                      </Button>
                    </>
                  ) : (
                    <>
                      <span className="w-1/3 shrink-0 font-mono text-sm font-medium">
                        {envVar.name}
                      </span>
                      <span className="text-muted-foreground">=</span>
                      <span className="min-w-0 break-all font-mono text-sm">{envVar.value}</span>
                    </>
                  )}
                </div>
              ))}
            </CardContent>
          </Card>
        )}
      </div>

      {/* Delete — hidden for protected app types (e.g. ProxyService) */}
      {!isProtected && (
        <div className="border-t pt-6">
          <Dialog>
            <DialogTrigger
              render={
                <Button variant="destructive" size="sm">
                  <Trash2 className="mr-1 h-4 w-4" />
                  Delete App
                </Button>
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
                <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
                <Button
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
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>
      )}
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

// -- Helpers --

type ConfigFormState = {
  displayName: string;
  installDirectory: string;
  commandLine: string;
  arguments: string;
  workingDirectory: string;
  restartPolicyId: string;
  healthEndpoint: string;
  updateCommand: string;
  updateTimeoutSeconds: string;
  autoStart: boolean;
  environmentVariables: { name: string; value: string }[];
};

type ConfigField = {
  label: string;
  value: string;
  field?: keyof ConfigFormState;
  readOnly?: boolean;
  isBoolean?: boolean;
  booleanValue?: boolean;
  isSelect?: boolean;
  selectOptions?: { id: string; name: string; displayName: string }[];
};

function buildFormState(app: AppDetail): ConfigFormState {
  return {
    displayName: app.displayName,
    installDirectory: app.installDirectory,
    commandLine: app.commandLine,
    arguments: app.arguments ?? '',
    workingDirectory: app.workingDirectory ?? '',
    restartPolicyId: app.restartPolicyName,
    healthEndpoint: app.healthEndpoint ?? '',
    updateCommand: app.updateCommand ?? '',
    updateTimeoutSeconds: app.updateTimeoutSeconds?.toString() ?? '',
    autoStart: app.autoStart,
    environmentVariables: app.environmentVariables.map((envVar) => ({ ...envVar })),
  };
}
