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

function AutoStartDisplay({
  displayName,
  resolved,
  defaults,
  hasOverrides,
}: CapabilityWidgetProps) {
  const enabled = Boolean(resolved['enabled']);

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="flex items-center justify-between">
          <span className="text-sm text-muted-foreground">Auto-Start</span>
          <div className="flex items-center gap-2">
            <span className="text-sm font-medium">{enabled ? 'Enabled' : 'Disabled'}</span>
            {hasOverrides && (
              <ConfigSourceIndicator source={getFieldSource('enabled', resolved, defaults)} />
            )}
          </div>
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { AutoStartDisplay };
