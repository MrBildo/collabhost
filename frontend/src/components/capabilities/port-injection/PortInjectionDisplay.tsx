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

function PortInjectionDisplay({
  displayName,
  resolved,
  defaults,
  hasOverrides,
}: CapabilityWidgetProps) {
  const environmentVariableName = String(resolved['environmentVariableName'] ?? '');
  const portFormat = String(resolved['portFormat'] ?? '');

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Environment Variable</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium font-mono">{environmentVariableName}</span>
              {hasOverrides && (
                <ConfigSourceIndicator
                  source={getFieldSource('environmentVariableName', resolved, defaults)}
                />
              )}
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Port Format</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium font-mono">{portFormat}</span>
              {hasOverrides && (
                <ConfigSourceIndicator source={getFieldSource('portFormat', resolved, defaults)} />
              )}
            </div>
          </div>
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { PortInjectionDisplay };
