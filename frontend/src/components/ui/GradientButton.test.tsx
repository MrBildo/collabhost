import { describe, test, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { GradientButton } from './GradientButton';

describe('GradientButton', () => {
  test('renders with gradient-button data slot', () => {
    render(<GradientButton>Click me</GradientButton>);
    const button = screen.getByRole('button', { name: 'Click me' });
    expect(button).toHaveAttribute('data-slot', 'gradient-button');
  });

  test('calls onClick when clicked', async () => {
    const user = userEvent.setup();
    const handleClick = vi.fn();
    render(<GradientButton onClick={handleClick}>Click</GradientButton>);
    await user.click(screen.getByRole('button'));
    expect(handleClick).toHaveBeenCalledOnce();
  });

  test('applies custom className', () => {
    render(<GradientButton className="my-class">Button</GradientButton>);
    expect(screen.getByRole('button')).toHaveClass('my-class');
  });

  test('is disabled when disabled prop is true', () => {
    render(<GradientButton disabled>Disabled</GradientButton>);
    expect(screen.getByRole('button')).toBeDisabled();
  });
});
