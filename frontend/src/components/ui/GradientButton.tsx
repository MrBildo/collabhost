import { Button as ButtonPrimitive } from '@base-ui/react/button';
import { cva, type VariantProps } from 'class-variance-authority';

import { cn } from '@/lib/utils';

const gradientButtonVariants = cva(
  'inline-flex shrink-0 items-center justify-center font-medium whitespace-nowrap transition-all outline-none select-none focus-visible:ring-3 focus-visible:ring-ring/50 active:translate-y-px disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0',
  {
    variants: {
      variant: {
        primary: [
          'bg-[image:var(--gradient-primary)] text-primary-foreground',
          'hover:brightness-110 hover:shadow-[0_4px_15px_rgba(var(--tw-color-primary),0.3)]',
          'border border-transparent',
        ].join(' '),
        ghost: [
          'bg-transparent text-foreground',
          'hover:bg-muted/50',
          'border border-transparent',
        ].join(' '),
        destructive: [
          'bg-[image:var(--gradient-destructive)] text-destructive-foreground',
          'hover:brightness-110',
          'border border-transparent',
        ].join(' '),
        outline: [
          'bg-transparent text-foreground',
          'border border-[var(--glass-border)]',
          'hover:bg-muted/30',
        ].join(' '),
      },
      size: {
        default:
          "h-9 gap-2 px-4 text-sm rounded-[var(--glass-radius-button)] [&_svg:not([class*='size-'])]:size-4",
        sm: "h-7 gap-1.5 px-3 text-xs rounded-[var(--glass-radius-button)] [&_svg:not([class*='size-'])]:size-3.5",
        lg: "h-11 gap-2 px-6 text-base rounded-[var(--glass-radius-button)] [&_svg:not([class*='size-'])]:size-5",
        icon: 'size-9 rounded-[var(--glass-radius-button)]',
        'icon-sm': 'size-7 rounded-[var(--glass-radius-button)]',
      },
    },
    defaultVariants: {
      variant: 'primary',
      size: 'default',
    },
  },
);

type GradientButtonProps = ButtonPrimitive.Props & VariantProps<typeof gradientButtonVariants>;

function GradientButton({
  className,
  variant = 'primary',
  size = 'default',
  ...props
}: GradientButtonProps) {
  return (
    <ButtonPrimitive
      data-slot="gradient-button"
      className={cn(gradientButtonVariants({ variant, size, className }))}
      {...props}
    />
  );
}

export { GradientButton, gradientButtonVariants };
