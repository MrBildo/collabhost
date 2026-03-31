import { useCallback, useMemo } from 'react';
import { Plus, Trash2 } from 'lucide-react';

import { Input } from '@/components/ui/input';
import { GradientButton } from '@/components/ui/GradientButton';

import type { CapabilityWidgetProps } from '../types';

type EnvRow = {
  key: string;
  value: string;
  isInherited: boolean;
  isOverridden: boolean;
};

function EnvironmentDefaultsWidget({ resolved, defaults, onChange }: CapabilityWidgetProps) {
  const typeDefaults = useMemo(
    () => (defaults['defaults'] ?? {}) as Record<string, string>,
    [defaults],
  );

  const resolvedDefaults = useMemo(
    () => (resolved['defaults'] ?? {}) as Record<string, string>,
    [resolved],
  );

  const rows: EnvRow[] = useMemo(() => {
    const allKeys = new Set([...Object.keys(typeDefaults), ...Object.keys(resolvedDefaults)]);
    return Array.from(allKeys)
      .sort()
      .map((key) => {
        const isInherited = key in typeDefaults;
        const isOverridden = isInherited ? resolvedDefaults[key] !== typeDefaults[key] : true;
        return {
          key,
          value: resolvedDefaults[key] ?? '',
          isInherited,
          isOverridden,
        };
      });
  }, [typeDefaults, resolvedDefaults]);

  const emitOverrides = useCallback(
    (newResolvedDefaults: Record<string, string>) => {
      if (!onChange) return;

      const overrideDefaults: Record<string, string> = {};
      let hasChanges = false;

      for (const [key, value] of Object.entries(newResolvedDefaults)) {
        if (!(key in typeDefaults) || typeDefaults[key] !== value) {
          overrideDefaults[key] = value;
          hasChanges = true;
        }
      }

      onChange(hasChanges ? { defaults: overrideDefaults } : null);
    },
    [onChange, typeDefaults],
  );

  const handleValueChange = useCallback(
    (key: string, newValue: string) => {
      emitOverrides({ ...resolvedDefaults, [key]: newValue });
    },
    [resolvedDefaults, emitOverrides],
  );

  const handleRemoveOverride = useCallback(
    (key: string) => {
      const updated = { ...resolvedDefaults };

      if (key in typeDefaults) {
        updated[key] = typeDefaults[key];
      } else {
        delete updated[key];
      }

      emitOverrides(updated);
    },
    [resolvedDefaults, typeDefaults, emitOverrides],
  );

  const handleAddRow = useCallback(() => {
    const newKey = '';
    emitOverrides({ ...resolvedDefaults, [newKey]: '' });
  }, [resolvedDefaults, emitOverrides]);

  const handleKeyChange = useCallback(
    (oldKey: string, newKey: string) => {
      if (oldKey === newKey) return;

      const updated: Record<string, string> = {};
      for (const [key, value] of Object.entries(resolvedDefaults)) {
        if (key === oldKey) {
          updated[newKey] = value;
        } else {
          updated[key] = value;
        }
      }

      emitOverrides(updated);
    },
    [resolvedDefaults, emitOverrides],
  );

  return (
    <div className="space-y-3">
      {rows.length > 0 && (
        <div className="space-y-2">
          {rows.map((row) => (
            <div key={row.key} className="flex items-center gap-2">
              <Input
                value={row.key}
                placeholder="VARIABLE_NAME"
                disabled={row.isInherited}
                className="flex-1 font-mono text-xs"
                onChange={(event) => handleKeyChange(row.key, event.target.value)}
              />
              <Input
                value={row.value}
                placeholder="value"
                disabled={row.isInherited && !row.isOverridden}
                className={`flex-1 font-mono text-xs ${
                  row.isInherited && !row.isOverridden ? 'opacity-50' : ''
                }`}
                onChange={(event) => handleValueChange(row.key, event.target.value)}
              />
              {row.isOverridden && (
                <GradientButton
                  variant="ghost"
                  size="icon-sm"
                  onClick={() => handleRemoveOverride(row.key)}
                  aria-label={`Remove override for ${row.key}`}
                >
                  <Trash2 className="size-3.5" />
                </GradientButton>
              )}
              {!row.isOverridden && <div className="w-7 shrink-0" />}
            </div>
          ))}
        </div>
      )}

      <GradientButton variant="outline" size="sm" onClick={handleAddRow}>
        <Plus className="size-3.5" />
        Add Variable
      </GradientButton>
    </div>
  );
}

export { EnvironmentDefaultsWidget };
