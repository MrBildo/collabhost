import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';

import type { CapabilityWidgetProps } from '../types';

function NodeRuntimeDisplay({ displayName, resolved }: CapabilityWidgetProps) {
  const nodeVersion = String(resolved['nodeVersion'] ?? '');
  const packageManager = String(resolved['packageManager'] ?? '');
  const buildCommand = String(resolved['buildCommand'] ?? '');

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="space-y-3">
          {nodeVersion && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Node Version</span>
              <span className="text-sm font-medium font-mono">{nodeVersion}</span>
            </div>
          )}

          {packageManager && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Package Manager</span>
              <span className="text-sm font-medium">{packageManager}</span>
            </div>
          )}

          {buildCommand && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Build Command</span>
              <span className="text-sm font-medium font-mono">{buildCommand}</span>
            </div>
          )}
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { NodeRuntimeDisplay };
