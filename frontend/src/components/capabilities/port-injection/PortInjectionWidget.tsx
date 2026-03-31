import { useCallback } from 'react';

import { cn } from '@/lib/utils';
import { Input } from '@/components/ui/input';

import type { CapabilityWidgetProps } from '../types';

function PortInjectionWidget({ resolved, defaults, onChange }: CapabilityWidgetProps) {
  const environmentVariableName = String(resolved['environmentVariableName'] ?? '');
  const portFormat = String(resolved['portFormat'] ?? '');

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
        <label className="text-sm font-medium">Environment Variable Name</label>
        <Input
          value={environmentVariableName}
          placeholder="e.g. PORT"
          className={cn(isFieldOverridden('environmentVariableName') && 'border-accent')}
          onChange={(event) => handleFieldChange('environmentVariableName', event.target.value)}
        />
      </div>

      <div className="space-y-1.5">
        <label className="text-sm font-medium">Port Format</label>
        <Input
          value={portFormat}
          placeholder="e.g. {port}"
          className={cn(isFieldOverridden('portFormat') && 'border-accent')}
          onChange={(event) => handleFieldChange('portFormat', event.target.value)}
        />
      </div>
    </div>
  );
}

export { PortInjectionWidget };
