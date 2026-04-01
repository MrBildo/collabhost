import { useDiscoveryStrategies, useLookupLabel } from '@/hooks/useLookups';

import type { CapabilityWidgetProps } from '../types';

function ProcessSummary({ resolved }: CapabilityWidgetProps) {
  const discoveryStrategy = String(resolved['discoveryStrategy'] ?? '');
  const { data: strategies } = useDiscoveryStrategies();
  const { getDisplayLabel } = useLookupLabel(strategies);

  return (
    <span className="inline-flex items-center rounded-full bg-muted/50 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
      {getDisplayLabel(discoveryStrategy)}
    </span>
  );
}

export { ProcessSummary };
