import { ConfigSourceIndicator } from '@/components/ui/ConfigSourceIndicator';
import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';

import type { CapabilityWidgetProps } from '../types';

const SERVE_MODE_LABELS: Record<string, string> = {
  reverseProxy: 'Reverse Proxy',
  fileServer: 'File Server',
};

function getFieldSource(
  fieldName: string,
  resolved: Record<string, unknown>,
  defaults: Record<string, unknown>,
): 'inherited' | 'overridden' {
  return resolved[fieldName] !== defaults[fieldName] ? 'overridden' : 'inherited';
}

function RoutingDisplay({ displayName, resolved, defaults, hasOverrides }: CapabilityWidgetProps) {
  const domainPattern = String(resolved['domainPattern'] ?? '');
  const serveMode = String(resolved['serveMode'] ?? '');
  const spaFallback = Boolean(resolved['spaFallback']);

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Domain Pattern</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium font-mono">{domainPattern || '--'}</span>
              {hasOverrides && (
                <ConfigSourceIndicator
                  source={getFieldSource('domainPattern', resolved, defaults)}
                />
              )}
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Serve Mode</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">{SERVE_MODE_LABELS[serveMode] ?? serveMode}</span>
              {hasOverrides && (
                <ConfigSourceIndicator source={getFieldSource('serveMode', resolved, defaults)} />
              )}
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">SPA Fallback</span>
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium">{spaFallback ? 'Yes' : 'No'}</span>
              {hasOverrides && (
                <ConfigSourceIndicator source={getFieldSource('spaFallback', resolved, defaults)} />
              )}
            </div>
          </div>
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { RoutingDisplay };
