import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';

import type { CapabilityWidgetProps } from '../types';

function ReactRuntimeDisplay({ displayName, resolved }: CapabilityWidgetProps) {
  const reactVersion = String(resolved['reactVersion'] ?? '');
  const router = String(resolved['router'] ?? '');
  const bundler = String(resolved['bundler'] ?? '');

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="space-y-3">
          {reactVersion && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">React Version</span>
              <span className="text-sm font-medium font-mono">{reactVersion}</span>
            </div>
          )}

          {router && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Router</span>
              <span className="text-sm font-medium">{router}</span>
            </div>
          )}

          {bundler && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Bundler</span>
              <span className="text-sm font-medium">{bundler}</span>
            </div>
          )}
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { ReactRuntimeDisplay };
