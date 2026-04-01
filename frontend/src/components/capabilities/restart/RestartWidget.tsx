import { useCallback } from 'react';

import { cn } from '@/lib/utils';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useFieldOptions } from '@/hooks/useFieldOptions';

import type { CapabilityWidgetProps } from '../types';

function RestartWidget({ resolved, defaults, onChange }: CapabilityWidgetProps) {
  const policy = String(resolved['policy'] ?? 'never');
  const { options } = useFieldOptions('restart', 'policy');

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
        <label className="text-sm font-medium">Restart Policy</label>
        <Select value={policy} onValueChange={(value) => handleFieldChange('policy', value)}>
          <SelectTrigger className={cn('w-full', isFieldOverridden('policy') && 'border-accent')}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {options.map((option) => (
              <SelectItem key={option.value} value={option.value}>
                {option.displayName}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </div>
  );
}

export { RestartWidget };
