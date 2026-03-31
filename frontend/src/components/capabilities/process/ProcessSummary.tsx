import type { CapabilityWidgetProps } from '../types';

const DISCOVERY_STRATEGY_LABELS: Record<string, string> = {
  'dotnet-runtimeconfig': '.NET',
  'package-json': 'npm',
  manual: 'Manual',
};

function ProcessSummary({ resolved }: CapabilityWidgetProps) {
  const discoveryStrategy = String(resolved['discoveryStrategy'] ?? '');
  const label = DISCOVERY_STRATEGY_LABELS[discoveryStrategy] ?? discoveryStrategy;

  return (
    <span className="inline-flex items-center rounded-full bg-muted/50 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-muted-foreground">
      {label}
    </span>
  );
}

export { ProcessSummary };
