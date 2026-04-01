import { ConfigSourceIndicator } from '@/components/ui/ConfigSourceIndicator';
import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';
import { useFieldOptions } from '@/hooks/useFieldOptions';

import type { CapabilityWidgetProps } from '../types';

function getFieldSource(
  fieldName: string,
  resolved: Record<string, unknown>,
  defaults: Record<string, unknown>,
): 'inherited' | 'overridden' {
  return resolved[fieldName] !== defaults[fieldName] ? 'overridden' : 'inherited';
}

function RestartDisplay({ displayName, resolved, defaults, hasOverrides }: CapabilityWidgetProps) {
  const policy = String(resolved['policy'] ?? '');
  const { getDisplayLabel } = useFieldOptions('restart', 'policy');

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="flex items-center justify-between">
          <span className="text-sm text-muted-foreground">Policy</span>
          <div className="flex items-center gap-2">
            <span className="text-sm font-medium">{getDisplayLabel(policy)}</span>
            {hasOverrides && (
              <ConfigSourceIndicator source={getFieldSource('policy', resolved, defaults)} />
            )}
          </div>
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { RestartDisplay };
