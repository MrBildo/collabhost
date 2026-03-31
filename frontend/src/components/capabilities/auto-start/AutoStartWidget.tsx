import { useCallback } from 'react';

import type { CapabilityWidgetProps } from '../types';

function AutoStartWidget({ resolved, defaults, onChange }: CapabilityWidgetProps) {
  const enabled = Boolean(resolved['enabled']);

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
      <div className="flex items-center justify-between">
        <label className="text-sm font-medium">Auto-Start on Collabhost Startup</label>
        <button
          type="button"
          role="switch"
          aria-checked={enabled}
          onClick={() => handleFieldChange('enabled', !enabled)}
          className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors ${
            enabled ? 'bg-primary' : 'bg-muted'
          }`}
        >
          <span
            className={`pointer-events-none block h-4 w-4 rounded-full bg-background shadow-sm transition-transform ${
              enabled ? 'translate-x-4' : 'translate-x-0'
            }`}
          />
        </button>
      </div>
    </div>
  );
}

export { AutoStartWidget };
