import { Moon, Sun } from 'lucide-react';
import { useTheme } from '@/hooks/useTheme';
import { cn } from '@/lib/utils';

type ThemeToggleProps = {
  className?: string;
};

function ThemeToggle({ className }: ThemeToggleProps) {
  const { theme, toggleTheme } = useTheme();

  return (
    <button
      data-slot="theme-toggle"
      type="button"
      onClick={toggleTheme}
      aria-label={theme === 'light' ? 'Switch to dark mode' : 'Switch to light mode'}
      className={cn(
        'relative inline-flex h-8 w-14 shrink-0 items-center rounded-full',
        'border border-[var(--glass-border)]',
        'bg-muted/50 transition-colors',
        'hover:bg-muted/80',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background',
        className,
      )}
    >
      <span
        className={cn(
          'inline-flex h-6 w-6 items-center justify-center rounded-full transition-transform duration-200',
          'bg-[image:var(--gradient-primary)] text-primary-foreground shadow-sm',
          theme === 'dark' ? 'translate-x-7' : 'translate-x-1',
        )}
      >
        {theme === 'light' ? <Sun className="h-3.5 w-3.5" /> : <Moon className="h-3.5 w-3.5" />}
      </span>
    </button>
  );
}

export { ThemeToggle };
