import { describe, test, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { StatusBadge } from './StatusBadge';
import type { ProcessState } from '@/types/api';

describe('StatusBadge', () => {
  const states: ProcessState[] = [
    'Running',
    'Stopped',
    'Crashed',
    'Starting',
    'Stopping',
    'Restarting',
    'Unknown',
  ];

  test.each(states)('renders %s status with correct label', (status) => {
    render(<StatusBadge status={status} />);
    expect(screen.getByText(status)).toBeInTheDocument();
  });

  test('sets data-status attribute', () => {
    render(<StatusBadge status="Running" />);
    const badge = screen.getByText('Running').closest('[data-slot="status-badge"]');
    expect(badge).toHaveAttribute('data-status', 'Running');
  });

  test('applies pulse animation for transitioning states', () => {
    const { container } = render(<StatusBadge status="Starting" />);
    const dot = container.querySelector('[aria-hidden="true"]');
    expect(dot?.className).toContain('animate-');
  });

  test('does not apply pulse animation for stable states', () => {
    const { container } = render(<StatusBadge status="Running" />);
    const dot = container.querySelector('[aria-hidden="true"]');
    expect(dot?.className).not.toContain('animate-');
  });

  test('applies custom className', () => {
    render(<StatusBadge status="Running" className="my-custom" />);
    const badge = screen.getByText('Running').closest('[data-slot="status-badge"]');
    expect(badge).toHaveClass('my-custom');
  });
});
