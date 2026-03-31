import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeToggle } from './ThemeToggle';

// Mock the useTheme hook
const mockToggleTheme = vi.fn();
let mockTheme = 'light';

vi.mock('@/hooks/useTheme', () => ({
  useTheme: () => ({
    theme: mockTheme,
    toggleTheme: mockToggleTheme,
    setTheme: vi.fn(),
  }),
}));

describe('ThemeToggle', () => {
  beforeEach(() => {
    mockTheme = 'light';
    mockToggleTheme.mockReset();
  });

  test('renders with theme-toggle data slot', () => {
    render(<ThemeToggle />);
    const toggle = screen.getByRole('button');
    expect(toggle).toHaveAttribute('data-slot', 'theme-toggle');
  });

  test('shows correct aria-label for light mode', () => {
    mockTheme = 'light';
    render(<ThemeToggle />);
    expect(screen.getByLabelText('Switch to dark mode')).toBeInTheDocument();
  });

  test('shows correct aria-label for dark mode', () => {
    mockTheme = 'dark';
    render(<ThemeToggle />);
    expect(screen.getByLabelText('Switch to light mode')).toBeInTheDocument();
  });

  test('calls toggleTheme when clicked', async () => {
    const user = userEvent.setup();
    render(<ThemeToggle />);
    await user.click(screen.getByRole('button'));
    expect(mockToggleTheme).toHaveBeenCalledOnce();
  });

  test('applies custom className', () => {
    render(<ThemeToggle className="extra" />);
    expect(screen.getByRole('button')).toHaveClass('extra');
  });
});
