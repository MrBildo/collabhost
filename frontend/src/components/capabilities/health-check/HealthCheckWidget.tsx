import { useCallback } from 'react';

import { cn } from '@/lib/utils';
import { Input } from '@/components/ui/input';

import type { CapabilityWidgetProps } from '../types';

function HealthCheckWidget({ resolved, defaults, onChange }: CapabilityWidgetProps) {
  const endpoint = String(resolved['endpoint'] ?? '');
  const intervalSeconds = Number(resolved['intervalSeconds'] ?? 30);
  const timeoutSeconds = Number(resolved['timeoutSeconds'] ?? 5);
  const retries = resolved['retries'] != null ? Number(resolved['retries']) : null;

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
        <label className="text-sm font-medium">Health Endpoint</label>
        <Input
          value={endpoint}
          placeholder="/health"
          className={cn(isFieldOverridden('endpoint') && 'border-accent')}
          onChange={(event) => handleFieldChange('endpoint', event.target.value)}
        />
      </div>

      <div className="space-y-1.5">
        <label className="text-sm font-medium">Interval (seconds)</label>
        <Input
          type="number"
          min={1}
          value={intervalSeconds}
          className={cn(isFieldOverridden('intervalSeconds') && 'border-accent')}
          onChange={(event) => handleFieldChange('intervalSeconds', Number(event.target.value))}
        />
      </div>

      <div className="space-y-1.5">
        <label className="text-sm font-medium">Timeout (seconds)</label>
        <Input
          type="number"
          min={1}
          value={timeoutSeconds}
          className={cn(isFieldOverridden('timeoutSeconds') && 'border-accent')}
          onChange={(event) => handleFieldChange('timeoutSeconds', Number(event.target.value))}
        />
      </div>

      <div className="space-y-1.5">
        <label className="text-sm font-medium">Retries</label>
        <Input
          type="number"
          min={0}
          value={retries ?? ''}
          placeholder="Not set"
          className={cn(isFieldOverridden('retries') && 'border-accent')}
          onChange={(event) => {
            const raw = event.target.value;
            handleFieldChange('retries', raw === '' ? null : Number(raw));
          }}
        />
      </div>
    </div>
  );
}

export { HealthCheckWidget };
