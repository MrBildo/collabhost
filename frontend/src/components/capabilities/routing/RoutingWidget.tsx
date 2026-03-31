import { useCallback } from 'react';

import { cn } from '@/lib/utils';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

import type { CapabilityWidgetProps } from '../types';

const SERVE_MODES = [
  { value: 'reverse-proxy', label: 'Reverse Proxy' },
  { value: 'file-server', label: 'File Server' },
] as const;

function RoutingWidget({ resolved, defaults, onChange }: CapabilityWidgetProps) {
  const domainPattern = String(resolved['domainPattern'] ?? '');
  const serveMode = String(resolved['serveMode'] ?? 'reverse-proxy');
  const spaFallback = Boolean(resolved['spaFallback']);

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

      <div className="space-y-1.5">
        <label className="text-sm font-medium">Serve Mode</label>
        <Select value={serveMode} onValueChange={(value) => handleFieldChange('serveMode', value)}>
          <SelectTrigger
            className={cn('w-full', isFieldOverridden('serveMode') && 'border-accent')}
          >
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {SERVE_MODES.map((mode) => (
              <SelectItem key={mode.value} value={mode.value}>
                {mode.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

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
    </div>
  );
}

export { RoutingWidget };
