import { describe, test, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ArtifactDisplay } from './ArtifactDisplay';

describe('ArtifactDisplay', () => {
  const defaultProps = {
    displayName: 'Artifact',
    resolved: { location: 'C:\\apps\\myapp' },
    defaults: { location: '' },
    hasOverrides: true,
  };

  test('renders display name', () => {
    render(<ArtifactDisplay {...defaultProps} />);
    expect(screen.getByText('Artifact')).toBeInTheDocument();
  });

  test('renders location path when configured', () => {
    render(<ArtifactDisplay {...defaultProps} />);
    expect(screen.getByText('C:\\apps\\myapp')).toBeInTheDocument();
  });

  test('renders "Not configured" when location is empty', () => {
    render(
      <ArtifactDisplay
        {...defaultProps}
        resolved={{ location: '' }}
        defaults={{ location: '' }}
        hasOverrides={false}
      />,
    );
    expect(screen.getByText('Not configured')).toBeInTheDocument();
  });

  test('shows overridden indicator when location differs from default', () => {
    render(<ArtifactDisplay {...defaultProps} />);
    expect(screen.getByText('Overridden')).toBeInTheDocument();
  });

  test('shows inherited indicator when location matches default', () => {
    render(
      <ArtifactDisplay
        {...defaultProps}
        resolved={{ location: '' }}
        defaults={{ location: '' }}
        hasOverrides={true}
      />,
    );
    expect(screen.getByText('Inherited')).toBeInTheDocument();
  });

  test('does not show config source indicator when no overrides', () => {
    render(
      <ArtifactDisplay
        {...defaultProps}
        resolved={{ location: 'C:\\apps\\myapp' }}
        defaults={{ location: 'C:\\apps\\myapp' }}
        hasOverrides={false}
      />,
    );
    expect(screen.queryByText('Overridden')).not.toBeInTheDocument();
    expect(screen.queryByText('Inherited')).not.toBeInTheDocument();
  });
});
