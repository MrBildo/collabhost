import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DirectoryPicker } from './directory-picker';

// Mock the api module
const mockGet = vi.fn();
vi.mock('@/lib/api', () => ({
  api: {
    get: (...args: unknown[]) => mockGet(...args),
    interceptors: { request: { use: vi.fn() } },
  },
  getAdminKey: vi.fn(),
}));

describe('DirectoryPicker', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockGet.mockResolvedValue({
      data: {
        currentPath: 'C:\\',
        parent: null,
        entries: [
          { name: 'Projects', path: 'C:\\Projects' },
          { name: 'Program Files', path: 'C:\\Program Files' },
        ],
      },
    });
  });

  test('renders input with placeholder', () => {
    render(<DirectoryPicker value="" onChange={vi.fn()} placeholder="Pick a dir" />);
    expect(screen.getByPlaceholderText('Pick a dir')).toBeInTheDocument();
  });

  test('renders Browse button', () => {
    render(<DirectoryPicker value="" onChange={vi.fn()} />);
    expect(screen.getByText('Browse')).toBeInTheDocument();
  });

  test('renders with provided value', () => {
    const testPath = String.raw`C:\apps`;
    render(<DirectoryPicker value={testPath} onChange={vi.fn()} />);
    const input = screen.getByPlaceholderText('Enter directory path...');
    expect(input).toHaveValue(testPath);
  });

  test('calls onChange when typing in input', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(<DirectoryPicker value="" onChange={onChange} />);
    const input = screen.getByPlaceholderText('Enter directory path...');
    await user.type(input, 'C');

    expect(onChange).toHaveBeenCalledWith('C');
  });

  test('shows dropdown entries after typing a path separator', async () => {
    const user = userEvent.setup();

    render(<DirectoryPicker value="" onChange={vi.fn()} />);
    const input = screen.getByPlaceholderText('Enter directory path...');
    await user.type(input, 'C:\\');

    // Wait for debounced API call and dropdown to appear
    await waitFor(
      () => {
        expect(screen.getByText('Projects')).toBeInTheDocument();
      },
      { timeout: 1000 },
    );
  });

  test('calls onChange when selecting a dropdown entry', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();

    render(<DirectoryPicker value="" onChange={onChange} />);
    const input = screen.getByPlaceholderText('Enter directory path...');
    await user.type(input, 'C:\\');

    // Wait for dropdown
    await waitFor(
      () => {
        expect(screen.getByText('Projects')).toBeInTheDocument();
      },
      { timeout: 1000 },
    );

    await user.click(screen.getByText('Projects'));
    expect(onChange).toHaveBeenCalledWith('C:\\Projects');
  });

  test('disables input when disabled prop is true', () => {
    render(<DirectoryPicker value="" onChange={vi.fn()} disabled />);
    const input = screen.getByPlaceholderText('Enter directory path...');
    expect(input).toBeDisabled();
  });
});
