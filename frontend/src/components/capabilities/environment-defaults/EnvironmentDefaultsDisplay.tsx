import { ConfigSourceIndicator } from '@/components/ui/ConfigSourceIndicator';
import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';

import type { CapabilityWidgetProps } from '../types';

function getEnvVarEntries(
  resolved: Record<string, unknown>,
): Array<{ key: string; value: string }> {
  const defaults = resolved['defaults'];
  if (defaults == null || typeof defaults !== 'object') return [];

  return Object.entries(defaults as Record<string, string>).map(([key, value]) => ({
    key,
    value: String(value),
  }));
}

function getKeySource(
  key: string,
  resolved: Record<string, unknown>,
  defaults: Record<string, unknown>,
): 'inherited' | 'overridden' {
  const resolvedDefaults = (resolved['defaults'] ?? {}) as Record<string, string>;
  const typeDefaults = (defaults['defaults'] ?? {}) as Record<string, string>;

  if (!(key in typeDefaults)) return 'overridden';
  if (resolvedDefaults[key] !== typeDefaults[key]) return 'overridden';
  return 'inherited';
}

function EnvironmentDefaultsDisplay({
  displayName,
  resolved,
  defaults,
  hasOverrides,
}: CapabilityWidgetProps) {
  const entries = getEnvVarEntries(resolved);

  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{displayName}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        {entries.length === 0 ? (
          <p className="text-sm text-muted-foreground">No environment variables configured.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-muted/30">
                  <th className="pb-2 text-left font-medium text-muted-foreground">Variable</th>
                  <th className="pb-2 text-left font-medium text-muted-foreground">Value</th>
                  {hasOverrides && (
                    <th className="pb-2 text-right font-medium text-muted-foreground">Source</th>
                  )}
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => (
                  <tr key={entry.key} className="border-b border-muted/10 last:border-0">
                    <td className="py-2 pr-4 font-mono text-xs">{entry.key}</td>
                    <td className="py-2 pr-4 font-mono text-xs break-all">{entry.value}</td>
                    {hasOverrides && (
                      <td className="py-2 text-right">
                        <ConfigSourceIndicator
                          source={getKeySource(entry.key, resolved, defaults)}
                        />
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </GlassCardContent>
    </GlassCard>
  );
}

export { EnvironmentDefaultsDisplay };
