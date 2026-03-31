import { describe, test, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { TypeBadge } from './TypeBadge';

describe('TypeBadge', () => {
  test('renders the type name text', () => {
    render(<TypeBadge typeName="Executable" />);
    expect(screen.getByText('Executable')).toBeInTheDocument();
  });

  test('applies type-specific styling for known types', () => {
    render(<TypeBadge typeName="NpmPackage" />);
    const badge = screen.getByText('NpmPackage');
    expect(badge).toHaveAttribute('data-slot', 'type-badge');
  });

  test('handles unknown type names gracefully', () => {
    render(<TypeBadge typeName="CustomType" />);
    expect(screen.getByText('CustomType')).toBeInTheDocument();
  });

  test('applies custom className', () => {
    render(<TypeBadge typeName="StaticSite" className="extra" />);
    expect(screen.getByText('StaticSite')).toHaveClass('extra');
  });
});
