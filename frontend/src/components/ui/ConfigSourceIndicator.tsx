import { cn } from '@/lib/utils';

type ConfigSource = 'inherited' | 'overridden';

type ConfigSourceIndicatorProps = {
  source: ConfigSource;
  className?: string;
};

function ConfigSourceIndicator({ source, className }: ConfigSourceIndicatorProps) {
  const isOverridden = source === 'overridden';

  return (
    <span
      data-slot="config-source-indicator"
      data-source={source}
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider',
        isOverridden
          ? 'bg-accent/15 text-accent border border-accent/20'
          : 'bg-muted/50 text-muted-foreground border border-muted-foreground/10',
        className,
      )}
    >
      {isOverridden ? 'Overridden' : 'Inherited'}
    </span>
  );
}

export { ConfigSourceIndicator };
export type { ConfigSource };
