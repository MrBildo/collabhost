import * as React from 'react';

import { cn } from '@/lib/utils';

type GlassCardProps = React.ComponentProps<'div'> & {
  /** Padding density */
  size?: 'default' | 'sm';
  /** Disable the glassmorphism backdrop effect */
  disableGlass?: boolean;
};

function GlassCard({
  className,
  size = 'default',
  disableGlass = false,
  ...props
}: GlassCardProps) {
  return (
    <div
      data-slot="glass-card"
      data-size={size}
      className={cn(
        'group/glass-card flex flex-col gap-4 overflow-hidden text-sm text-card-foreground',
        'rounded-[var(--glass-radius-card)]',
        'border border-[var(--glass-border)]',
        !disableGlass && ['bg-[image:var(--glass-bg)]', 'backdrop-blur-[var(--glass-blur)]'],
        disableGlass && 'bg-card',
        'data-[size=default]:py-4',
        'data-[size=sm]:gap-3 data-[size=sm]:py-3',
        className,
      )}
      {...props}
    />
  );
}

function GlassCardHeader({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="glass-card-header"
      className={cn(
        'grid auto-rows-min items-start gap-1 px-5',
        'group-data-[size=sm]/glass-card:px-4',
        className,
      )}
      {...props}
    />
  );
}

function GlassCardTitle({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="glass-card-title"
      className={cn(
        'text-base leading-snug font-semibold group-data-[size=sm]/glass-card:text-sm',
        className,
      )}
      style={{ fontFamily: "'Space Grotesk', sans-serif" }}
      {...props}
    />
  );
}

function GlassCardDescription({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="glass-card-description"
      className={cn('text-sm text-muted-foreground', className)}
      {...props}
    />
  );
}

function GlassCardContent({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="glass-card-content"
      className={cn('px-5 group-data-[size=sm]/glass-card:px-4', className)}
      {...props}
    />
  );
}

function GlassCardFooter({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="glass-card-footer"
      className={cn('flex items-center px-5 pt-2 group-data-[size=sm]/glass-card:px-4', className)}
      {...props}
    />
  );
}

export {
  GlassCard,
  GlassCardHeader,
  GlassCardTitle,
  GlassCardDescription,
  GlassCardContent,
  GlassCardFooter,
};
