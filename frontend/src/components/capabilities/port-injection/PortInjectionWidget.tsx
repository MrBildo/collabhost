import type { CapabilityWidgetProps } from '../types';

function PortInjectionWidget({ resolved }: CapabilityWidgetProps) {
  const environmentVariableName = String(resolved['environmentVariableName'] ?? '');
  const portFormat = String(resolved['portFormat'] ?? '');

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <span className="text-sm text-muted-foreground">Environment Variable</span>
        <span className="text-sm font-medium font-mono">{environmentVariableName || '--'}</span>
      </div>

      <div className="flex items-center justify-between">
        <span className="text-sm text-muted-foreground">Port Format</span>
        <span className="text-sm font-medium font-mono">{portFormat || '--'}</span>
      </div>
    </div>
  );
}

export { PortInjectionWidget };
