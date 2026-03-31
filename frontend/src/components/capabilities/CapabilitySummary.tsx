import { getCapabilityComponents } from './registry';
import type { CapabilityEntry } from './types';

type CapabilitySummaryProps = {
  /** List of capabilities from the app. Only those with a Summary component are rendered. */
  capabilities: CapabilityEntry[];
};

function CapabilitySummary({ capabilities }: CapabilitySummaryProps) {
  const summaryCapabilities = capabilities.filter((capability) => {
    const family = getCapabilityComponents(capability.slug);
    return family.Summary != null;
  });

  if (summaryCapabilities.length === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {summaryCapabilities.map((capability) => {
        const family = getCapabilityComponents(capability.slug);
        const SummaryComponent = family.Summary;

        if (SummaryComponent == null) return null;

        return (
          <SummaryComponent
            key={capability.slug}
            displayName={capability.displayName}
            resolved={capability.resolved}
            defaults={capability.defaults}
            hasOverrides={capability.hasOverrides}
          />
        );
      })}
    </div>
  );
}

export { CapabilitySummary };
export type { CapabilitySummaryProps };
