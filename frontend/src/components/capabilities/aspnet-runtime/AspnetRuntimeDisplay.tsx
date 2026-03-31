import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';

import type { CapabilityWidgetProps } from '../types';

function AspnetRuntimeDisplay({ displayName, resolved }: CapabilityWidgetProps) {
  const targetFramework = String(resolved['targetFramework'] ?? '');
  const runtimeVersion = String(resolved['runtimeVersion'] ?? '');
  const isSelfContained = Boolean(resolved['selfContained']);

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <div className="flex flex-wrap items-center gap-2">
          {targetFramework && (
            <span className="inline-flex items-center rounded-full bg-purple-500/10 px-2.5 py-0.5 text-xs font-medium text-purple-700 dark:text-purple-400">
              {targetFramework}
            </span>
          )}
          {runtimeVersion && (
            <span className="inline-flex items-center rounded-full bg-blue-500/10 px-2.5 py-0.5 text-xs font-medium text-blue-700 dark:text-blue-400">
              {runtimeVersion}
            </span>
          )}
          <span
            className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
              isSelfContained
                ? 'bg-green-500/10 text-green-700 dark:text-green-400'
                : 'bg-muted/50 text-muted-foreground'
            }`}
          >
            {isSelfContained ? 'Self-Contained' : 'Framework-Dependent'}
          </span>
        </div>
      </GlassCardContent>
    </GlassCard>
  );
}

export { AspnetRuntimeDisplay };
