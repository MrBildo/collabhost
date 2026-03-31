import { useMemo } from 'react';

import { getCapabilityComponents } from './registry';
import type { CapabilityEntry } from './types';

type CapabilityPanelProps = {
  /** List of capabilities with resolved data from the API. */
  capabilities: CapabilityEntry[];
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

function renderCapabilityDisplay(capability: CapabilityEntry) {
  const family = getCapabilityComponents(capability.slug);

  return (
    <family.Display
      key={capability.slug}
      displayName={capability.displayName}
      resolved={capability.resolved}
      defaults={capability.defaults}
      hasOverrides={capability.hasOverrides}
    />
  );
}

function CapabilityPanel({ capabilities }: CapabilityPanelProps) {
  const { behavioral, informational } = useMemo(
    () => groupByCategory(capabilities),
    [capabilities],
  );

  return (
    <div className="space-y-8">
      {behavioral.length > 0 && (
        <section className="space-y-4">
          <h3
            className="text-base font-semibold"
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            Configuration
          </h3>
          <div className="space-y-4">{behavioral.map(renderCapabilityDisplay)}</div>
        </section>
      )}

      {informational.length > 0 && (
        <section className="space-y-4">
          <h3
            className="text-base font-semibold"
            style={{ fontFamily: "'Space Grotesk', sans-serif" }}
          >
            Runtime Info
          </h3>
          <div className="space-y-4">{informational.map(renderCapabilityDisplay)}</div>
        </section>
      )}
    </div>
  );
}

export { CapabilityPanel };
export type { CapabilityPanelProps };
