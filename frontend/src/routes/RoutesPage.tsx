import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Check, Loader2, RefreshCw, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useReloadCaddy, useRoutes } from '@/hooks/useRoutes';

type ReloadFeedback = 'success' | 'error' | null;

export function RoutesPage() {
  const { data, isLoading } = useRoutes();
  const reloadCaddy = useReloadCaddy();
  const [reloadFeedback, setReloadFeedback] = useState<ReloadFeedback>(null);

  useEffect(() => {
    if (reloadFeedback === null) return;

    const timer = setTimeout(() => {
      setReloadFeedback(null);
    }, 2000);

    return () => clearTimeout(timer);
  }, [reloadFeedback]);

  function handleReload() {
    reloadCaddy.mutate(undefined, {
      onSuccess: () => {
        setReloadFeedback('success');
        toast.success('Caddy configuration reloaded');
      },
      onError: () => {
        setReloadFeedback('error');
        toast.error('Failed to reload Caddy');
      },
    });
  }

  return (
    <div className="p-6">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-bold tracking-tight">Routes</h1>
        <div className="flex items-center gap-2">
          {reloadFeedback === 'success' && (
            <span className="text-sm text-green-600 dark:text-green-400">Caddy reloaded</span>
          )}
          {reloadFeedback === 'error' && (
            <span className="text-sm text-destructive">Reload failed</span>
          )}
          <Button
            variant="outline"
            size="sm"
            onClick={handleReload}
            disabled={reloadCaddy.isPending}
          >
            {reloadCaddy.isPending ? (
              <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-1.5 h-4 w-4" />
            )}
            Reload Caddy
          </Button>
        </div>
      </div>

      {isLoading ? (
        <RoutesTableSkeleton />
      ) : data?.routes.length === 0 ? (
        <div className="rounded-lg border bg-card p-12 text-center">
          <p className="text-muted-foreground">No routes configured</p>
        </div>
      ) : (
        <div className="rounded-lg border bg-card">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>App</TableHead>
                <TableHead>Domain</TableHead>
                <TableHead>Target</TableHead>
                <TableHead>Type</TableHead>
                <TableHead>HTTPS</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data?.routes.map((route) => (
                <TableRow key={route.appExternalId}>
                  <TableCell>
                    <Link
                      to={`/apps/${route.appExternalId}`}
                      className="font-medium text-primary hover:underline"
                    >
                      {route.appName}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <span className="font-mono text-sm text-primary">{route.domain}</span>
                  </TableCell>
                  <TableCell>
                    <span className="font-mono text-sm text-muted-foreground">{route.target}</span>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary">{route.proxyMode}</Badge>
                  </TableCell>
                  <TableCell>
                    {route.https ? (
                      <Check className="h-4 w-4 text-green-600 dark:text-green-400" />
                    ) : (
                      <X className="h-4 w-4 text-destructive" />
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}

function RoutesTableSkeleton() {
  return (
    <div className="rounded-lg border bg-card">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>App</TableHead>
            <TableHead>Domain</TableHead>
            <TableHead>Target</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>HTTPS</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: 3 }, (_, index) => (
            <TableRow key={index}>
              <TableCell>
                <Skeleton className="h-4 w-24" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-48" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-32" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-20" />
              </TableCell>
              <TableCell>
                <Skeleton className="h-4 w-4" />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
