import { useCallback } from 'react';

import { cn } from '@/lib/utils';
import { DirectoryPicker } from '@/components/ui/DirectoryPicker';

import type { CapabilityWidgetProps } from '../types';

function ArtifactWidget({ resolved, defaults, onChange }: CapabilityWidgetProps) {
  const location = String(resolved['location'] ?? '');

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
        <label className="text-sm font-medium">Location</label>
        <div className={cn(isFieldOverridden('location') && '[&_[data-slot=input]]:border-accent')}>
          <DirectoryPicker
            value={location}
            onChange={(path) => handleFieldChange('location', path)}
            placeholder="e.g. C:\apps\myapp"
            disabled={!onChange}
          />
        </div>
      </div>
    </div>
  );
}

export { ArtifactWidget };
