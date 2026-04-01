import { Link } from 'react-router-dom';
import { Plus } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { GradientButton } from '@/components/ui/GradientButton';
import { Skeleton } from '@/components/ui/skeleton';
import {
  GlassCard,
  GlassCardContent,
  GlassCardDescription,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';
import { useAppTypeList } from '@/hooks/useAppTypes';

function AppTypeCardSkeleton() {
  return (
    <div className="flex flex-col gap-3 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <Skeleton className="h-5 w-32" />
      <Skeleton className="h-4 w-48" />
      <div className="flex gap-1.5">
        <Skeleton className="h-5 w-16" />
        <Skeleton className="h-5 w-20" />
      </div>
    </div>
  );
}

export default function AppTypesListPage() {
  const { data: appTypes, isLoading, error } = useAppTypeList();

  return (
    <div className="p-6">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-bold tracking-tight">App Types</h1>
        <GradientButton render={<Link to="/app-types/new" />}>
          <Plus className="h-4 w-4" />
          New App Type
        </GradientButton>
      </div>

      {isLoading && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, index) => (
            <AppTypeCardSkeleton key={index} />
          ))}
        </div>
      )}

      {error && (
        <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm text-destructive">
          Failed to load app types. Please check that the API is running and try again.
        </div>
      )}

      {appTypes && appTypes.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 text-center">
          <p className="text-lg font-medium">No app types registered yet</p>
          <p className="mt-1 text-sm text-muted-foreground">
            Create your first app type to get started.
          </p>
          <GradientButton className="mt-4" size="lg" render={<Link to="/app-types/new" />}>
            <Plus className="h-5 w-5" />
            New App Type
          </GradientButton>
        </div>
      )}

      {appTypes && appTypes.length > 0 && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {appTypes.map((appType) => {
            const capabilitySlugs = Object.keys(appType.capabilities);

            return (
              <Link key={appType.id} to={`/app-types/${appType.id}`}>
                <GlassCard className="transition-shadow hover:shadow-md">
                  <GlassCardHeader>
                    <div className="flex items-center gap-2">
                      <GlassCardTitle>{appType.displayName}</GlassCardTitle>
                      {appType.isBuiltIn && (
                        <Badge variant="secondary" className="text-xs">
                          Built-in
                        </Badge>
                      )}
                    </div>
                    {appType.description && (
                      <GlassCardDescription>{appType.description}</GlassCardDescription>
                    )}
                  </GlassCardHeader>
                  <GlassCardContent>
                    {capabilitySlugs.length > 0 ? (
                      <div className="flex flex-wrap gap-1.5">
                        {capabilitySlugs.map((slug) => (
                          <Badge key={slug} variant="outline" className="text-xs">
                            {appType.capabilities[slug].displayName}
                          </Badge>
                        ))}
                      </div>
                    ) : (
                      <p className="text-xs text-muted-foreground">No capabilities configured</p>
                    )}
                  </GlassCardContent>
                </GlassCard>
              </Link>
            );
          })}
        </div>
      )}
    </div>
  );
}
