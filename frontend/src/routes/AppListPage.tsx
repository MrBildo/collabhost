import { Skeleton } from '@/components/ui/skeleton';
import { AppCard } from '@/components/AppCard';
import { useApps } from '@/hooks/useApps';

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
        <Skeleton className="h-3 w-20" />
      </div>
      <div className="border-t bg-muted/50 p-4">
        <Skeleton className="h-8 w-8" />
      </div>
    </div>
  );
}

export function AppListPage() {
  const { data: apps, isLoading, error } = useApps();

  return (
    <div className="p-6">
      <h1 className="mb-6 text-2xl font-bold tracking-tight">Apps</h1>

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
            Register an app via the API to get started
          </p>
        </div>
      )}

      {apps && apps.length > 0 && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {apps.map((app) => (
            <AppCard key={app.externalId} app={app} />
          ))}
        </div>
      )}
    </div>
  );
}
