import { ConfigSourceIndicator } from '@/components/ui/ConfigSourceIndicator';
import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';

import type { CapabilityWidgetProps } from '../types';

const DISCOVERY_STRATEGY_LABELS: Record<string, string> = {
  'dotnet-runtimeconfig': '.NET Runtime Config',
  'package-json': 'package.json',
  manual: 'Manual',
};

function getFieldSource(
  fieldName: string,
  resolved: Record<string, unknown>,
  defaults: Record<string, unknown>,
): 'inherited' | 'overridden' {
  return resolved[fieldName] !== defaults[fieldName] ? 'overridden' : 'inherited';
}

function ProcessDisplay({ displayName, resolved, defaults, hasOverrides }: CapabilityWidgetProps) {
  const discoveryStrategy = String(resolved['discoveryStrategy'] ?? '');
  const gracefulShutdown = Boolean(resolved['gracefulShutdown']);
  const shutdownTimeoutSeconds = Number(resolved['shutdownTimeoutSeconds'] ?? 0);

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Discovery Strategy</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">
                {DISCOVERY_STRATEGY_LABELS[discoveryStrategy] ?? discoveryStrategy}
              </span>
              {hasOverrides && (
                <ConfigSourceIndicator
                  source={getFieldSource('discoveryStrategy', resolved, defaults)}
                />
              )}
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Graceful Shutdown</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">{gracefulShutdown ? 'Yes' : 'No'}</span>
              {hasOverrides && (
                <ConfigSourceIndicator
                  source={getFieldSource('gracefulShutdown', resolved, defaults)}
                />
              )}
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Shutdown Timeout</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">{shutdownTimeoutSeconds}s</span>
              {hasOverrides && (
                <ConfigSourceIndicator
                  source={getFieldSource('shutdownTimeoutSeconds', resolved, defaults)}
                />
              )}
            </div>
          </div>

          {discoveryStrategy === 'manual' && resolved['command'] != null && (
            <>
              <div className="flex items-center justify-between">
                <span className="text-sm text-muted-foreground">Command</span>
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium font-mono">
                    {String(resolved['command'])}
                  </span>
                  {hasOverrides && (
                    <ConfigSourceIndicator source={getFieldSource('command', resolved, defaults)} />
                  )}
                </div>
              </div>
              {resolved['args'] && (
                <div className="flex items-center justify-between">
                  <span className="text-sm text-muted-foreground">Arguments</span>
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium font-mono">
                      {String(resolved['args'])}
                    </span>
                    {hasOverrides && (
                      <ConfigSourceIndicator source={getFieldSource('args', resolved, defaults)} />
                    )}
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { ProcessDisplay };
