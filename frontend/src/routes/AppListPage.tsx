import { Link } from 'react-router-dom';
import { ExternalLink, Loader2, Play, Plus, Square } from 'lucide-react';
import { toast } from 'sonner';
import { Skeleton } from '@/components/ui/skeleton';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import {
  GlassCard,
  GlassCardContent,
  GlassCardFooter,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';
import { GradientButton } from '@/components/ui/GradientButton';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { TypeBadge } from '@/components/ui/TypeBadge';
import { CapabilitySummary } from '@/components/capabilities/CapabilitySummary';
import { useApps, useStartApp, useStopApp } from '@/hooks/useApps';
import { toCapabilityEntries } from '@/lib/utils';
import type { AppResponse, ProcessState } from '@/types/api';

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

function AppCardSkeleton() {
  return (
    <div className="flex flex-col gap-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <div className="space-y-2 px-4">
        <div className="flex items-center justify-between">
          <Skeleton className="h-5 w-32" />
          <Skeleton className="h-5 w-20" />
        </div>
      </div>
      <div className="space-y-2 px-4">
        <Skeleton className="h-4 w-24" />
        <Skeleton className="h-4 w-40" />
      </div>
      <div className="px-4 pt-2">
        <Skeleton className="h-8 w-8" />
      </div>
    </div>
  );
}

type AppCardProps = {
  app: AppResponse;
};

function AppCard({ app }: AppCardProps) {
  const startApp = useStartApp();
  const stopApp = useStopApp();

  const status = deriveDisplayStatus(app);
  const isTransitioning = status === 'Starting' || status === 'Stopping' || status === 'Restarting';
  const isMutating = startApp.isPending || stopApp.isPending;
  const isDisabled = isTransitioning || isMutating;

  const domain = app.runtime.route?.domain ?? null;
  const isRouteActive = app.runtime.route?.state === 'active';

  const capabilities = toCapabilityEntries(app.capabilities);

  function handleStart(event: React.MouseEvent) {
    event.preventDefault();
    event.stopPropagation();
    startApp.mutate(app.id, {
      onSuccess: () => toast.success(`${app.displayName} started`),
      onError: () => toast.error(`Failed to start ${app.displayName}`),
    });
  }

  function handleStop(event: React.MouseEvent) {
    event.preventDefault();
    event.stopPropagation();
    stopApp.mutate(app.id, {
      onSuccess: () => toast.success(`${app.displayName} stopped`),
      onError: () => toast.error(`Failed to stop ${app.displayName}`),
    });
  }

  return (
    <Link to={`/apps/${app.id}`}>
      <GlassCard className="cursor-pointer transition-shadow hover:shadow-md">
        <GlassCardHeader>
          <div className="flex items-center justify-between">
            <GlassCardTitle>{app.displayName}</GlassCardTitle>
            <TypeBadge typeName={app.appType.displayName} />
          </div>
          <div className="flex items-center gap-2 pt-1">
            <StatusBadge status={status} />
          </div>
        </GlassCardHeader>

        <GlassCardContent className="space-y-2">
          {domain && (
            <div className="flex items-center gap-1 text-sm">
              <a
                href={`https://${domain}`}
                target="_blank"
                rel="noopener noreferrer"
                onClick={(event) => event.stopPropagation()}
                className={
                  isRouteActive
                    ? 'inline-flex items-center gap-1 text-primary hover:underline'
                    : 'inline-flex items-center gap-1 text-muted-foreground'
                }
              >
                {domain}
                <ExternalLink className="h-3 w-3" />
              </a>
            </div>
          )}

          <CapabilitySummary capabilities={capabilities} />
        </GlassCardContent>

        <GlassCardFooter className="gap-1">
          {isTransitioning || isMutating ? (
            <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
          ) : (
            <>
              {(status === 'Stopped' || status === 'Crashed') && (
                <Tooltip>
                  <TooltipTrigger
                    render={
                      <GradientButton
                        variant="ghost"
                        size="icon-sm"
                        onClick={handleStart}
                        disabled={isDisabled}
                      />
                    }
                  >
                    <Play className="h-4 w-4" />
                  </TooltipTrigger>
                  <TooltipContent>Start</TooltipContent>
                </Tooltip>
              )}

              {status === 'Running' && (
                <Tooltip>
                  <TooltipTrigger
                    render={
                      <GradientButton
                        variant="ghost"
                        size="icon-sm"
                        onClick={handleStop}
                        disabled={isDisabled}
                      />
                    }
                  >
                    <Square className="h-4 w-4" />
                  </TooltipTrigger>
                  <TooltipContent>Stop</TooltipContent>
                </Tooltip>
              )}
            </>
          )}
        </GlassCardFooter>
      </GlassCard>
    </Link>
  );
}

export function AppListPage() {
  const { data: apps, isLoading, error } = useApps();

  return (
    <div className="p-6">
      <div className="mb-6 flex items-center justify-between">
        <h1
          className="text-2xl font-bold tracking-tight"
          style={{ fontFamily: "'Space Grotesk', sans-serif" }}
        >
          Apps
        </h1>
        <GradientButton render={<Link to="/apps/new" />}>
          <Plus className="h-4 w-4" />
          Add App
        </GradientButton>
      </div>

      {isLoading && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, index) => (
            <AppCardSkeleton key={index} />
          ))}
        </div>
      )}

      {error && (
        <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm text-destructive">
          Failed to load apps. Please check that the API is running and try again.
        </div>
      )}

      {apps && apps.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 text-center">
          <p className="text-lg font-medium">No apps registered yet</p>
          <p className="mt-1 text-sm text-muted-foreground">
            Add your first application to get started.
          </p>
          <GradientButton className="mt-4" size="lg" render={<Link to="/apps/new" />}>
            <Plus className="h-5 w-5" />
            Add App
          </GradientButton>
        </div>
      )}

      {apps && apps.length > 0 && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {apps.map((app) => (
            <AppCard key={app.id} app={app} />
          ))}
        </div>
      )}
    </div>
  );
}
