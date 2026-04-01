import { useCallback } from 'react';

import { cn } from '@/lib/utils';
import { Input } from '@/components/ui/input';
import { useFieldOptions } from '@/hooks/useFieldOptions';

import type { CapabilityWidgetProps } from '../types';

function RoutingWidget({ resolved, defaults, onChange }: CapabilityWidgetProps) {
  const domainPattern = String(resolved['domainPattern'] ?? '');
  const serveMode = String(resolved['serveMode'] ?? 'reverseProxy');
  const spaFallback = Boolean(resolved['spaFallback']);
  const { getDisplayLabel: getServeModeLabel } = useFieldOptions('routing', 'serveMode');

  const isFieldOverridden = useCallback(
    (field: string): boolean => {
      return resolved[field] !== defaults[field];
    },
    [resolved, defaults],
  );

  const handleFieldChange = useCallback(
    (field: string, value: unknown) => {
      if (!onChange) return;

      const currentOverrides: Record<string, unknown> = {};

      for (const key of Object.keys(resolved)) {
        if (resolved[key] !== defaults[key]) {
          currentOverrides[key] = resolved[key];
        }
      }

      if (value === defaults[field]) {
        delete currentOverrides[field];
      } else {
        currentOverrides[field] = value;
      }

      onChange(Object.keys(currentOverrides).length === 0 ? null : currentOverrides);
    },
    [onChange, resolved, defaults],
  );

  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <label className="text-sm font-medium">Domain Pattern</label>
        <Input
          value={domainPattern}
          placeholder="e.g. {app}.collab.internal"
          className={cn(isFieldOverridden('domainPattern') && 'border-accent')}
          onChange={(event) => handleFieldChange('domainPattern', event.target.value)}
        />
      </div>

      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">Serve Mode</span>
        <span className="inline-flex items-center rounded-full bg-muted/50 px-2.5 py-0.5 text-xs font-medium text-muted-foreground">
          {getServeModeLabel(serveMode)}
        </span>
      </div>

      {serveMode === 'fileServer' && (
        <div className="flex items-center justify-between">
          <label className="text-sm font-medium">SPA Fallback</label>
          <button
            type="button"
            role="switch"
            aria-checked={spaFallback}
            onClick={() => handleFieldChange('spaFallback', !spaFallback)}
            className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors ${
              spaFallback ? 'bg-primary' : 'bg-muted'
            }`}
          >
            <span
              className={`pointer-events-none block h-4 w-4 rounded-full bg-background shadow-sm transition-transform ${
                spaFallback ? 'translate-x-4' : 'translate-x-0'
              }`}
            />
          </button>
        </div>
      )}
    </div>
  );
}

export { RoutingWidget };
