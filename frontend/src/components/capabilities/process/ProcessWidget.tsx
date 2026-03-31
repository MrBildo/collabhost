import { useCallback } from 'react';

import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

import type { CapabilityWidgetProps } from '../types';

const DISCOVERY_STRATEGIES = [
  { value: 'dotnet-runtimeconfig', label: '.NET Runtime Config' },
  { value: 'package-json', label: 'package.json' },
  { value: 'manual', label: 'Manual' },
] as const;

function ProcessWidget({ resolved, defaults, onChange }: CapabilityWidgetProps) {
  const discoveryStrategy = String(resolved['discoveryStrategy'] ?? 'manual');
  const gracefulShutdown = Boolean(resolved['gracefulShutdown']);
  const shutdownTimeoutSeconds = Number(resolved['shutdownTimeoutSeconds']);
  const command = String(resolved['command'] ?? '');
  const args = String(resolved['args'] ?? '');

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
        <label className="text-sm font-medium">Discovery Strategy</label>
        <Select
          value={discoveryStrategy}
          onValueChange={(value) => handleFieldChange('discoveryStrategy', value)}
        >
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {DISCOVERY_STRATEGIES.map((strategy) => (
              <SelectItem key={strategy.value} value={strategy.value}>
                {strategy.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {discoveryStrategy === 'manual' && (
        <>
          <div className="space-y-1.5">
            <label className="text-sm font-medium">Command</label>
            <Input
              value={command}
              placeholder="e.g. dotnet"
              onChange={(event) => handleFieldChange('command', event.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <label className="text-sm font-medium">Arguments</label>
            <Input
              value={args}
              placeholder="e.g. MyApp.dll"
              onChange={(event) => handleFieldChange('args', event.target.value)}
            />
          </div>
        </>
      )}

      <div className="flex items-center justify-between">
        <label className="text-sm font-medium">Graceful Shutdown</label>
        <button
          type="button"
          role="switch"
          aria-checked={gracefulShutdown}
          onClick={() => handleFieldChange('gracefulShutdown', !gracefulShutdown)}
          className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors ${
            gracefulShutdown ? 'bg-primary' : 'bg-muted'
          }`}
        >
          <span
            className={`pointer-events-none block h-4 w-4 rounded-full bg-background shadow-sm transition-transform ${
              gracefulShutdown ? 'translate-x-4' : 'translate-x-0'
            }`}
          />
        </button>
      </div>

      {gracefulShutdown && (
        <div className="space-y-1.5">
          <label className="text-sm font-medium">Shutdown Timeout (seconds)</label>
          <Input
            type="number"
            min={1}
            value={shutdownTimeoutSeconds}
            onChange={(event) =>
              handleFieldChange('shutdownTimeoutSeconds', Number(event.target.value))
            }
          />
        </div>
      )}
    </div>
  );
}

export { ProcessWidget };
