import { useNavigate } from 'react-router-dom';
import { Loader2, Play, RefreshCw, Square } from 'lucide-react';
import { toast } from 'sonner';
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { useAppStatus, useStartApp, useStopApp, useRestartApp } from '@/hooks/useApps';
import { APP_TYPE_NAMES, BASE_DOMAIN, STATUS_MAP } from '@/lib/constants';
import { formatUptime } from '@/lib/format';
import { cn } from '@/lib/utils';
import type { AppListItem, ProcessState } from '@/types/api';

type AppCardProps = {
  app: AppListItem;
};

export function AppCard({ app }: AppCardProps) {
  const navigate = useNavigate();
  const { data: status, isLoading: isStatusLoading } = useAppStatus(app.externalId);
  const startApp = useStartApp();
  const stopApp = useStopApp();
  const restartApp = useRestartApp();

  const isStaticSite = app.appTypeName === APP_TYPE_NAMES.STATIC_SITE;
  const processState: ProcessState = status?.processState ?? 'Stopped';
  const statusConfig = STATUS_MAP[processState];
  const isTransitioning =
    processState === 'Starting' || processState === 'Stopping' || processState === 'Restarting';
  const isMutating = startApp.isPending || stopApp.isPending || restartApp.isPending;
  const isDisabled = isTransitioning || isMutating;

  const handleCardClick = () => {
    navigate(`/apps/${app.externalId}`);
  };

  const handleStart = (event: React.MouseEvent) => {
    event.stopPropagation();
    startApp.mutate(app.externalId, {
      onSuccess: () => toast.success('App started'),
      onError: () => toast.error('Failed to start app'),
    });
  };

  const handleStop = (event: React.MouseEvent) => {
    event.stopPropagation();
    stopApp.mutate(app.externalId, {
      onSuccess: () => toast.success('App stopped'),
      onError: () => toast.error('Failed to stop app'),
    });
  };

  const handleRestart = (event: React.MouseEvent) => {
    event.stopPropagation();
    restartApp.mutate(app.externalId, {
      onSuccess: () => toast.success('App restarted'),
      onError: () => toast.error('Failed to restart app'),
    });
  };

  return (
    <Card
      className="cursor-pointer transition-shadow hover:shadow-md"
      onClick={handleCardClick}
      role="button"
      tabIndex={0}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault();
          handleCardClick();
        }
      }}
    >
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle className="font-bold" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>
            {app.displayName}
          </CardTitle>
          <Badge variant="secondary">{app.appTypeName}</Badge>
        </div>
      </CardHeader>

      <CardContent className="space-y-2">
        <div className="flex items-center gap-2">
          {isStatusLoading ? (
            <Loader2 className="h-3 w-3 animate-spin text-muted-foreground" />
          ) : (
            <div className={cn('h-2 w-2 rounded-full', statusConfig.color)} />
          )}
          <span className="text-sm">{statusConfig.label}</span>
        </div>

        <div className="text-sm text-primary">
          {app.name}.{BASE_DOMAIN}
        </div>

        <div className="flex items-center gap-4 text-xs text-muted-foreground">
          {app.port !== null && <span>Port: {app.port}</span>}
          {processState === 'Running' &&
            status?.uptimeSeconds !== null &&
            status?.uptimeSeconds !== undefined && (
              <span>Uptime: {formatUptime(status.uptimeSeconds)}</span>
            )}
        </div>
      </CardContent>

      <CardFooter className="gap-1">
        {isStaticSite ? (
          <span className="text-xs text-muted-foreground">Served by proxy</span>
        ) : isTransitioning || isMutating ? (
          <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
        ) : (
          <>
            {(processState === 'Stopped' || processState === 'Crashed') && (
              <Tooltip>
                <TooltipTrigger
                  render={
                    <Button variant="ghost" size="icon" onClick={handleStart} disabled={isDisabled}>
                      <Play className="h-4 w-4" />
                    </Button>
                  }
                />
                <TooltipContent>Start</TooltipContent>
              </Tooltip>
            )}

            {processState === 'Running' && (
              <Tooltip>
                <TooltipTrigger
                  render={
                    <Button variant="ghost" size="icon" onClick={handleStop} disabled={isDisabled}>
                      <Square className="h-4 w-4" />
                    </Button>
                  }
                />
                <TooltipContent>Stop</TooltipContent>
              </Tooltip>
            )}

            {(processState === 'Running' || processState === 'Crashed') && (
              <Tooltip>
                <TooltipTrigger
                  render={
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={handleRestart}
                      disabled={isDisabled}
                    >
                      <RefreshCw className="h-4 w-4" />
                    </Button>
                  }
                />
                <TooltipContent>Restart</TooltipContent>
              </Tooltip>
            )}
          </>
        )}
      </CardFooter>
    </Card>
  );
}
