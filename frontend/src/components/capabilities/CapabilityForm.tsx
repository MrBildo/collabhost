import { useCallback, useMemo, useState } from 'react';

import { getCapabilityComponents } from './registry';
import type { CapabilityEntry } from './types';

type CapabilityFormProps = {
  /** List of capabilities from the app type, with resolved values and defaults. */
  capabilities: CapabilityEntry[];
  /** Called when the form overrides change. Receives the full capabilityOverrides object. */
  onOverridesChange?: (overrides: Record<string, Record<string, unknown> | null>) => void;
};

function groupByCategory(capabilities: CapabilityEntry[]) {
  const behavioral: CapabilityEntry[] = [];
  const informational: CapabilityEntry[] = [];

  for (const capability of capabilities) {
    if (capability.category === 'behavioral') {
      behavioral.push(capability);
    } else {
      informational.push(capability);
    }
  }

  return { behavioral, informational };
}

function CapabilityForm({ capabilities, onOverridesChange }: CapabilityFormProps) {
  const [overrides, setOverrides] = useState<Record<string, Record<string, unknown> | null>>({});

  const { behavioral, informational } = useMemo(
    () => groupByCategory(capabilities),
    [capabilities],
  );

  const handleCapabilityChange = useCallback(
    (slug: string, capabilityOverrides: Record<string, unknown> | null) => {
      setOverrides((previous) => {
        const next = { ...previous };

        if (capabilityOverrides === null) {
          delete next[slug];
        } else {
          next[slug] = capabilityOverrides;
        }

        onOverridesChange?.(next);
        return next;
      });
    },
    [onOverridesChange],
  );

  const renderCapability = useCallback(
    (capability: CapabilityEntry) => {
      const family = getCapabilityComponents(capability.slug);

      const mergedResolved = { ...capability.resolved };
      const currentOverride = overrides[capability.slug];
      if (currentOverride) {
        Object.assign(mergedResolved, currentOverride);
      }

      return (
        <div key={capability.slug} className="space-y-2">
          <h4 className="text-sm font-semibold">{capability.displayName}</h4>
          <family.Widget
            resolved={mergedResolved}
            defaults={capability.defaults}
            hasOverrides={capability.hasOverrides || capability.slug in overrides}
            onChange={(value) => handleCapabilityChange(capability.slug, value)}
          />
        </div>
      );
    },
    [overrides, handleCapabilityChange],
  );

  return (
    <div className="space-y-8">
      {behavioral.length > 0 && (
        <section className="space-y-6">
          <h3
            className="text-base font-semibold"
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            Configuration
          </h3>
          {behavioral.map(renderCapability)}
        </section>
      )}

      {informational.length > 0 && (
        <section className="space-y-6">
          <h3
            className="text-base font-semibold"
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            Runtime Info
          </h3>
          {informational.map(renderCapability)}
        </section>
      )}
    </div>
  );
}

export { CapabilityForm };
export type { CapabilityFormProps };
