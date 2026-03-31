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

function HealthCheckDisplay({
  displayName,
  resolved,
  defaults,
  hasOverrides,
}: CapabilityWidgetProps) {
  const endpoint = String(resolved['endpoint'] ?? '');
  const intervalSeconds = Number(resolved['intervalSeconds'] ?? 0);
  const timeoutSeconds = Number(resolved['timeoutSeconds'] ?? 0);
  const retries = resolved['retries'] != null ? Number(resolved['retries']) : null;

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Endpoint</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium font-mono">{endpoint}</span>
              {hasOverrides && (
                <ConfigSourceIndicator source={getFieldSource('endpoint', resolved, defaults)} />
              )}
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Interval</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">{intervalSeconds}s</span>
              {hasOverrides && (
                <ConfigSourceIndicator
                  source={getFieldSource('intervalSeconds', resolved, defaults)}
                />
              )}
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Timeout</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">{timeoutSeconds}s</span>
              {hasOverrides && (
                <ConfigSourceIndicator
                  source={getFieldSource('timeoutSeconds', resolved, defaults)}
                />
              )}
            </div>
          </div>

          {retries != null && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Retries</span>
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium">{retries}</span>
                {hasOverrides && (
                  <ConfigSourceIndicator source={getFieldSource('retries', resolved, defaults)} />
                )}
              </div>
            </div>
          )}
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { HealthCheckDisplay };
