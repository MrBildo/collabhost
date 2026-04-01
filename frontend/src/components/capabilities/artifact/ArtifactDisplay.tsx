import { Folder } from 'lucide-react';

import { ConfigSourceIndicator } from '@/components/ui/ConfigSourceIndicator';
import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';

import type { CapabilityWidgetProps } from '../types';

function getFieldSource(
  fieldName: string,
  resolved: Record<string, unknown>,
  defaults: Record<string, unknown>,
): 'inherited' | 'overridden' {
  return resolved[fieldName] !== defaults[fieldName] ? 'overridden' : 'inherited';
}

function ArtifactDisplay({ displayName, resolved, defaults, hasOverrides }: CapabilityWidgetProps) {
  const location = String(resolved['location'] ?? '');

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Location</span>
            <div className="flex items-center gap-2">
              {location ? (
                <span className="flex items-center gap-1.5 text-sm font-medium font-mono">
                  <Folder className="size-3.5 shrink-0 text-muted-foreground" />
                  {location}
                </span>
              ) : (
                <span className="text-sm text-muted-foreground italic">Not configured</span>
              )}
              {hasOverrides && (
                <ConfigSourceIndicator source={getFieldSource('location', resolved, defaults)} />
              )}
            </div>
          </div>
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { ArtifactDisplay };
