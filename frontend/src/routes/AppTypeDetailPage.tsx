import { useMemo } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { toast } from 'sonner';
import { ArrowLeft, Loader2, Trash2 } from 'lucide-react';
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
import { Skeleton } from '@/components/ui/skeleton';
import { CapabilityPanel } from '@/components/capabilities/CapabilityPanel';
import type { CapabilityEntry } from '@/components/capabilities/types';
import { useAppTypeDetail, useDeleteAppType } from '@/hooks/useAppTypes';

export default function AppTypeDetailPage() {
  const { id } = useParams<{ id: string }>();

  if (!id) {
    return (
      <div className="p-6">
        <p className="text-destructive">No app type ID provided.</p>
      </div>
    );
  }

  return <AppTypeDetailContent externalId={id} />;
}

type AppTypeDetailContentProps = {
  externalId: string;
};

function AppTypeDetailContent({ externalId }: AppTypeDetailContentProps) {
  const { data: appType, isLoading, error } = useAppTypeDetail(externalId);
  const deleteAppType = useDeleteAppType();
  const navigate = useNavigate();

  const capabilityEntries: CapabilityEntry[] = useMemo(() => {
    if (!appType) return [];

    return Object.entries(appType.capabilities).map(([slug, capability]) => ({
      slug,
      category: capability.category as 'behavioral' | 'informational',
      displayName: capability.displayName,
      resolved: capability.defaults,
      defaults: capability.defaults,
      hasOverrides: false,
    }));
  }, [appType]);

  if (isLoading) {
    return (
      <div className="p-6">
        <Skeleton className="mb-4 h-4 w-16" />
        <div className="mb-6 flex items-center gap-3">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-5 w-20" />
        </div>
        <Skeleton className="h-32 rounded-lg" />
      </div>
    );
  }

  if (error || !appType) {
    return (
      <div className="p-6">
        <Link
          to="/app-types"
          className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4" />
          App Types
        </Link>
        <div className="mt-4 rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm text-destructive">
          Failed to load app type details. The app type may have been deleted or the API is
          unavailable.
        </div>
      </div>
    );
  }

  return (
    <div className="p-6">
      <Link
        to="/app-types"
        className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" />
        App Types
      </Link>

      <div className="mb-6 mt-4 flex items-center gap-3">
        <h1
          className="text-2xl font-bold tracking-tight"
          style={{ fontFamily: "'Space Grotesk', sans-serif" }}
        >
          {appType.displayName}
        </h1>
        {appType.isBuiltIn && <Badge variant="secondary">Built-in</Badge>}
      </div>

      {appType.description && (
        <p className="mb-6 text-sm text-muted-foreground">{appType.description}</p>
      )}

      <Card className="mb-6">
        <CardContent className="divide-y p-0">
          <div className="grid grid-cols-3 gap-4 px-4 py-3">
            <span className="text-sm font-medium text-muted-foreground">Name (slug)</span>
            <span className="col-span-2 text-sm font-mono">{appType.name}</span>
          </div>
          <div className="grid grid-cols-3 gap-4 px-4 py-3">
            <span className="text-sm font-medium text-muted-foreground">Display Name</span>
            <span className="col-span-2 text-sm">{appType.displayName}</span>
          </div>
          <div className="grid grid-cols-3 gap-4 px-4 py-3">
            <span className="text-sm font-medium text-muted-foreground">Built-in</span>
            <span className="col-span-2 text-sm">{appType.isBuiltIn ? 'Yes' : 'No'}</span>
          </div>
        </CardContent>
      </Card>

      {capabilityEntries.length > 0 && (
        <div className="mb-6">
          <h2
            className="mb-4 text-lg font-semibold"
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            Capabilities
          </h2>
          <CapabilityPanel capabilities={capabilityEntries} />
        </div>
      )}

      {capabilityEntries.length === 0 && (
        <p className="mb-6 text-sm text-muted-foreground">
          No capabilities configured for this app type.
        </p>
      )}

      {!appType.isBuiltIn && (
        <div className="border-t pt-6">
          <Dialog>
            <DialogTrigger
              render={
                <Button variant="destructive" size="sm">
                  <Trash2 className="mr-1 h-4 w-4" />
                  Delete App Type
                </Button>
              }
            />
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Delete App Type</DialogTitle>
                <DialogDescription>
                  Are you sure you want to delete <strong>{appType.displayName}</strong>? This
                  action cannot be undone. Any apps using this type must be reassigned first.
                </DialogDescription>
              </DialogHeader>
              <DialogFooter>
                <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
                <Button
                  variant="destructive"
                  onClick={() =>
                    deleteAppType.mutate(externalId, {
                      onSuccess: () => {
                        toast.success('App type deleted');
                        navigate('/app-types');
                      },
                      onError: () => toast.error('Failed to delete app type'),
                    })
                  }
                  disabled={deleteAppType.isPending}
                >
                  {deleteAppType.isPending && <Loader2 className="mr-1 h-4 w-4 animate-spin" />}
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
