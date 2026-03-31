import { useMemo } from 'react';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';
import { useCapabilityCatalog } from '@/hooks/useCapabilities';
import type { CapabilityCatalogItem } from '@/types/api';

export default function CapabilitiesPage() {
  const { data: capabilities, isLoading, error } = useCapabilityCatalog();

  const { behavioral, informational } = useMemo(() => {
    const behavioralItems: CapabilityCatalogItem[] = [];
    const informationalItems: CapabilityCatalogItem[] = [];

    for (const capability of capabilities ?? []) {
      if (capability.category === 'behavioral') {
        behavioralItems.push(capability);
      } else {
        informationalItems.push(capability);
      }
    }

    return { behavioral: behavioralItems, informational: informationalItems };
  }, [capabilities]);

  return (
    <div className="p-6">
      <div className="mb-6">
        <h1 className="text-2xl font-bold tracking-tight">Capabilities</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Available capabilities that can be assigned to app types.
        </p>
      </div>

      {isLoading && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, index) => (
            <div key={index} className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
              <Skeleton className="mb-2 h-5 w-32" />
              <Skeleton className="h-4 w-48" />
            </div>
          ))}
        </div>
      )}

      {error && (
        <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm text-destructive">
          Failed to load capabilities. Please check that the API is running and try again.
        </div>
      )}

      {capabilities && capabilities.length === 0 && (
        <p className="text-sm text-muted-foreground">No capabilities registered.</p>
      )}

      {behavioral.length > 0 && (
        <section className="mb-8">
          <h2
            className="mb-4 text-base font-semibold"
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            Behavioral
          </h2>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
            {behavioral.map((capability) => (
              <CapabilityCard key={capability.slug} capability={capability} />
            ))}
          </div>
        </section>
      )}

      {informational.length > 0 && (
        <section>
          <h2
            className="mb-4 text-base font-semibold"
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            Informational
          </h2>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
            {informational.map((capability) => (
              <CapabilityCard key={capability.slug} capability={capability} />
            ))}
          </div>
        </section>
      )}
    </div>
  );
}

type CapabilityCardProps = {
  capability: CapabilityCatalogItem;
};

function CapabilityCard({ capability }: CapabilityCardProps) {
  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <div className="flex items-center gap-2">
          <GlassCardTitle>{capability.displayName}</GlassCardTitle>
          <Badge variant="outline" className="text-xs">
            {capability.category}
          </Badge>
        </div>
      </GlassCardHeader>
      <GlassCardContent>
        <p className="text-xs font-mono text-muted-foreground">{capability.slug}</p>
        {capability.description && (
          <p className="mt-1 text-sm text-muted-foreground">{capability.description}</p>
        )}
      </GlassCardContent>
    </GlassCard>
  );
}
