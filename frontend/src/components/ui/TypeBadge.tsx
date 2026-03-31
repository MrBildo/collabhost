import { cn } from '@/lib/utils';

type TypeBadgeProps = {
  typeName: string;
  className?: string;
};

type TypeStyle = {
  bgClass: string;
  textClass: string;
  borderClass: string;
};

const TYPE_STYLE_MAP: Record<string, TypeStyle> = {
  Executable: {
    bgClass: 'bg-primary/10',
    textClass: 'text-primary',
    borderClass: 'border-primary/20',
  },
  NpmPackage: {
    bgClass: 'bg-emerald-500/10',
    textClass: 'text-emerald-400',
    borderClass: 'border-emerald-500/20',
  },
  StaticSite: {
    bgClass: 'bg-violet-500/10',
    textClass: 'text-violet-400',
    borderClass: 'border-violet-500/20',
  },
};

const DEFAULT_TYPE_STYLE: TypeStyle = {
  bgClass: 'bg-secondary',
  textClass: 'text-secondary-foreground',
  borderClass: 'border-secondary-foreground/10',
};

function TypeBadge({ typeName, className }: TypeBadgeProps) {
  const style = TYPE_STYLE_MAP[typeName] ?? DEFAULT_TYPE_STYLE;

  return (
    <span
      data-slot="type-badge"
      className={cn(
        'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium',
        style.bgClass,
        style.textClass,
        style.borderClass,
        className,
      )}
    >
      {typeName}
    </span>
  );
}

export { TypeBadge, TYPE_STYLE_MAP };
