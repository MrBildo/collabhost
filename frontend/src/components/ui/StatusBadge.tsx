import { cn } from '@/lib/utils';
import type { ProcessState } from '@/types/api';

type StatusBadgeProps = {
  status: ProcessState;
  className?: string;
};

type StatusStyle = {
  label: string;
  dotClass: string;
  badgeClass: string;
  pulse: boolean;
};

const STATUS_STYLES: Record<ProcessState, StatusStyle> = {
  Running: {
    label: 'Running',
    dotClass: 'bg-success',
    badgeClass: 'bg-success/15 text-success border-success/20',
    pulse: false,
  },
  Stopped: {
    label: 'Stopped',
    dotClass: 'bg-muted-foreground/50',
    badgeClass: 'bg-muted/50 text-muted-foreground border-muted-foreground/10',
    pulse: false,
  },
  Crashed: {
    label: 'Crashed',
    dotClass: 'bg-destructive',
    badgeClass: 'bg-destructive/15 text-destructive border-destructive/20',
    pulse: false,
  },
  Starting: {
    label: 'Starting',
    dotClass: 'bg-amber-400',
    badgeClass: 'bg-amber-400/15 text-amber-400 border-amber-400/20',
    pulse: true,
  },
  Stopping: {
    label: 'Stopping',
    dotClass: 'bg-amber-400',
    badgeClass: 'bg-amber-400/15 text-amber-400 border-amber-400/20',
    pulse: true,
  },
  Restarting: {
    label: 'Restarting',
    dotClass: 'bg-amber-400',
    badgeClass: 'bg-amber-400/15 text-amber-400 border-amber-400/20',
    pulse: true,
  },
  Unknown: {
    label: 'Unknown',
    dotClass: 'bg-muted-foreground/50',
    badgeClass: 'bg-muted/50 text-muted-foreground border-muted-foreground/10',
    pulse: false,
  },
};

function StatusBadge({ status, className }: StatusBadgeProps) {
  const style = STATUS_STYLES[status];

  return (
    <span
      data-slot="status-badge"
      data-status={status}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium',
        style.badgeClass,
        className,
      )}
    >
      <span
        className={cn(
          'h-1.5 w-1.5 rounded-full',
          style.dotClass,
          style.pulse && 'animate-[status-pulse_2s_ease-in-out_infinite]',
        )}
        aria-hidden="true"
      />
      {style.label}
    </span>
  );
}

export { StatusBadge, STATUS_STYLES };
