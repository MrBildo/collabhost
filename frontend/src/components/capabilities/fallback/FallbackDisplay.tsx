import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';

import type { CapabilityWidgetProps } from '../types';

type FallbackDisplayProps = CapabilityWidgetProps & {
  /** The capability slug, shown as the card title. */
  slug: string;
};

function FallbackDisplay({ slug, resolved }: FallbackDisplayProps) {
  return (
    <GlassCard size="sm">
      <GlassCardHeader>
        <GlassCardTitle>{slug}</GlassCardTitle>
      </GlassCardHeader>
      <GlassCardContent>
        <pre className="overflow-x-auto rounded-md bg-muted/30 p-3 text-xs leading-relaxed text-muted-foreground">
          {JSON.stringify(resolved, null, 2)}
        </pre>
      </GlassCardContent>
    </GlassCard>
  );
}

export { FallbackDisplay };
