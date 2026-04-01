import { Boxes, Shield } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { clearAdminKey, getAdminKey } from '@/lib/api';
import { formatDateTime } from '@/lib/format';
import { useAppList, useSystemStatus } from '@/hooks/useSystem';

function maskKey(key: string): string {
  if (key.length <= 8) return key;
  return `${key.slice(0, 4)}...${key.slice(-4)}`;
}

export default function SystemPage() {
  const { data: status, isLoading: isStatusLoading } = useSystemStatus();
  const { data: apps, isLoading: isAppsLoading } = useAppList();

  const adminKey = getAdminKey();

  function handleClearKey() {
    clearAdminKey();
    window.location.reload();
  }

  return (
    <div className="p-6">
      <h1 className="mb-6 text-2xl font-bold tracking-tight">System</h1>

      <div className="grid gap-6 md:grid-cols-2">
        <StatusCard
          status={status?.status}
          version={status?.version}
          timestamp={status?.timestamp}
          isLoading={isStatusLoading}
        />
        <AppSummaryCard totalApps={apps?.length} isLoading={isAppsLoading} />
      </div>

      <div className="mt-6">
        <AdminKeyCard adminKey={adminKey} onClearKey={handleClearKey} />
      </div>
    </div>
  );
}

type StatusCardProps = {
  status: string | undefined;
  version: string | undefined;
  timestamp: string | undefined;
  isLoading: boolean;
};

function StatusCard({ status, version, timestamp, isLoading }: StatusCardProps) {
  const isHealthy = status === 'healthy';
  const dotColor = isHealthy ? 'bg-green-500' : 'bg-yellow-500';

  return (
    <div className="rounded-lg border bg-card p-6 text-card-foreground">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">Status</h2>
      {isLoading ? (
        <div className="space-y-3">
          <Skeleton className="h-4 w-32" />
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-4 w-48" />
        </div>
      ) : (
        <div className="space-y-3">
          <div className="flex items-center gap-2">
            <div className={`h-2 w-2 rounded-full ${dotColor}`} />
            <span className="text-sm font-medium capitalize">{status ?? 'Unknown'}</span>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Version</span>
            <span className="font-mono text-sm">{version ?? '-'}</span>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Last checked</span>
            <span className="text-sm">{timestamp ? formatDateTime(timestamp) : '-'}</span>
          </div>
        </div>
      )}
    </div>
  );
}

type AppSummaryCardProps = {
  totalApps: number | undefined;
  isLoading: boolean;
};

function AppSummaryCard({ totalApps, isLoading }: AppSummaryCardProps) {
  return (
    <div className="rounded-lg border bg-card p-6 text-card-foreground">
      <h2 className="mb-4 text-lg font-semibold tracking-tight">Apps</h2>
      {isLoading ? (
        <Skeleton className="h-10 w-16" />
      ) : (
        <div className="flex items-center gap-3">
          <Boxes className="h-8 w-8 text-primary" />
          <div>
            <span className="text-3xl font-bold">{totalApps ?? 0}</span>
            <p className="text-sm text-muted-foreground">Total registered</p>
          </div>
        </div>
      )}
    </div>
  );
}

type AdminKeyCardProps = {
  adminKey: string | null;
  onClearKey: () => void;
};

function AdminKeyCard({ adminKey, onClearKey }: AdminKeyCardProps) {
  return (
    <div className="rounded-lg border bg-card p-6 text-card-foreground">
      <div className="mb-4 flex items-center gap-2">
        <Shield className="h-5 w-5 text-primary" />
        <h2 className="text-lg font-semibold tracking-tight">Admin Key</h2>
      </div>
      <div className="flex items-center justify-between">
        <span className="font-mono text-sm text-muted-foreground">
          {adminKey ? maskKey(adminKey) : 'Not set'}
        </span>
        {adminKey && (
          <Button variant="outline" size="sm" onClick={onClearKey}>
            Clear Key
          </Button>
        )}
      </div>
    </div>
  );
}
