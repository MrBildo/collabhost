import { describe, test, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ConfigSourceIndicator } from './ConfigSourceIndicator';

describe('ConfigSourceIndicator', () => {
  test('renders Inherited label for inherited source', () => {
    render(<ConfigSourceIndicator source="inherited" />);
    expect(screen.getByText('Inherited')).toBeInTheDocument();
  });

  test('renders Overridden label for overridden source', () => {
    render(<ConfigSourceIndicator source="overridden" />);
    expect(screen.getByText('Overridden')).toBeInTheDocument();
  });

  test('sets data-source attribute', () => {
    render(<ConfigSourceIndicator source="inherited" />);
    const indicator = screen.getByText('Inherited');
    expect(indicator).toHaveAttribute('data-source', 'inherited');
  });

  test('applies custom className', () => {
    render(<ConfigSourceIndicator source="overridden" className="test-class" />);
    expect(screen.getByText('Overridden')).toHaveClass('test-class');
  });
});
