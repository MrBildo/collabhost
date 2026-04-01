import { describe, test, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ArtifactWidget } from './ArtifactWidget';

// Mock the api module to prevent real HTTP calls from DirectoryPicker
vi.mock('@/lib/api', () => ({
  api: {
    get: vi.fn().mockResolvedValue({ data: { currentPath: '', parent: null, entries: [] } }),
    interceptors: { request: { use: vi.fn() } },
  },
  getAdminKey: vi.fn(),
}));

describe('ArtifactWidget', () => {
  const defaultProps = {
    displayName: 'Artifact',
    resolved: { location: 'C:\\apps\\myapp' },
    defaults: { location: '' },
    hasOverrides: true,
  };

  test('renders Location label', () => {
    render(<ArtifactWidget {...defaultProps} />);
    expect(screen.getByText('Location')).toBeInTheDocument();
  });

  test('renders DirectoryPicker with current value', () => {
    render(<ArtifactWidget {...defaultProps} />);
    const input = screen.getByPlaceholderText('e.g. C:\\apps\\myapp');
    expect(input).toHaveValue('C:\\apps\\myapp');
  });

  test('renders Browse button', () => {
    render(<ArtifactWidget {...defaultProps} />);
    expect(screen.getByText('Browse')).toBeInTheDocument();
  });

  test('calls onChange with location override when value changes', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(<ArtifactWidget {...defaultProps} onChange={onChange} />);
    const input = screen.getByPlaceholderText('e.g. C:\\apps\\myapp');

    await user.clear(input);
    await user.type(input, 'D:\\new');

    // onChange should be called with location overrides
    expect(onChange).toHaveBeenCalled();
    const lastCall = onChange.mock.calls[onChange.mock.calls.length - 1];
    expect(lastCall[0]).toEqual({ location: 'D:\\new' });
  });

  test('calls onChange with null when value matches default', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(
      <ArtifactWidget
        {...defaultProps}
        resolved={{ location: 'X' }}
        defaults={{ location: '' }}
        onChange={onChange}
      />,
    );
    const input = screen.getByPlaceholderText('e.g. C:\\apps\\myapp');

    await user.clear(input);

    // Clearing to empty should match the default, so onChange(null)
    expect(onChange).toHaveBeenCalledWith(null);
  });

  test('disables DirectoryPicker when onChange is not provided', () => {
    render(<ArtifactWidget {...defaultProps} />);
    const input = screen.getByPlaceholderText('e.g. C:\\apps\\myapp');
    expect(input).toBeDisabled();
  });
});
