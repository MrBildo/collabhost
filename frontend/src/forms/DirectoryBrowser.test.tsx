import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, test, vi } from 'vitest'
import { DirectoryBrowser, buildBreadcrumbs } from './DirectoryBrowser'

// Mock the API endpoint
vi.mock('@/api/endpoints', () => ({
  browseFilesystem: vi.fn(),
}))

// Import the mock after vi.mock so we can control return values
import { browseFilesystem } from '@/api/endpoints'
const mockBrowse = vi.mocked(browseFilesystem)

// Polyfill showModal/close for jsdom
beforeEach(() => {
  if (!HTMLDialogElement.prototype.showModal) {
    HTMLDialogElement.prototype.showModal = function () {
      this.setAttribute('open', '')
    }
  }
  if (!HTMLDialogElement.prototype.close) {
    HTMLDialogElement.prototype.close = function () {
      this.removeAttribute('open')
    }
  }
  vi.clearAllMocks()
})

function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        gcTime: 0,
      },
    },
  })
}

function renderWithQuery(ui: React.ReactElement) {
  const queryClient = createQueryClient()
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>)
}

describe('DirectoryBrowser', () => {
  test('renders dialog when open', () => {
    mockBrowse.mockResolvedValue({
      currentPath: 'C:\\Projects',
      parent: 'C:\\',
      directories: [{ name: 'collab', path: 'C:\\Projects\\collab' }],
    })

    renderWithQuery(<DirectoryBrowser isOpen={true} initialPath="C:\\Projects" onSelect={vi.fn()} onCancel={vi.fn()} />)

    expect(screen.getByText('Browse Directory')).toBeInTheDocument()
    expect(screen.getByText('Select')).toBeInTheDocument()
    expect(screen.getByText('Cancel')).toBeInTheDocument()
  })

  test('does not show dialog when closed', () => {
    renderWithQuery(<DirectoryBrowser isOpen={false} initialPath="" onSelect={vi.fn()} onCancel={vi.fn()} />)

    const dialog = document.querySelector('dialog')
    expect(dialog).toBeInTheDocument()
    expect(dialog).not.toHaveAttribute('open')
  })

  test('displays directories from API response', async () => {
    mockBrowse.mockResolvedValue({
      currentPath: 'C:\\Projects',
      parent: 'C:\\',
      directories: [
        { name: 'collab', path: 'C:\\Projects\\collab' },
        { name: 'tools', path: 'C:\\Projects\\tools' },
      ],
    })

    renderWithQuery(<DirectoryBrowser isOpen={true} initialPath="C:\\Projects" onSelect={vi.fn()} onCancel={vi.fn()} />)

    await waitFor(() => {
      expect(screen.getByText('collab')).toBeInTheDocument()
    })
    expect(screen.getByText('tools')).toBeInTheDocument()
  })

  test('navigates into a directory on click', async () => {
    const user = userEvent.setup()
    mockBrowse
      .mockResolvedValueOnce({
        currentPath: 'C:\\Projects',
        parent: 'C:\\',
        directories: [
          { name: 'collab', path: 'C:\\Projects\\collab' },
          { name: 'tools', path: 'C:\\Projects\\tools' },
        ],
      })
      .mockResolvedValueOnce({
        currentPath: 'C:\\Projects\\collab',
        parent: 'C:\\Projects',
        directories: [{ name: 'collabhost', path: 'C:\\Projects\\collab\\collabhost' }],
      })

    renderWithQuery(<DirectoryBrowser isOpen={true} initialPath="C:\\Projects" onSelect={vi.fn()} onCancel={vi.fn()} />)

    await waitFor(() => {
      expect(screen.getByText('collab')).toBeInTheDocument()
    })

    await user.click(screen.getByText('collab'))

    await waitFor(() => {
      expect(screen.getByText('collabhost')).toBeInTheDocument()
    })
  })

  test('calls onSelect with current path when Select is clicked', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()

    mockBrowse.mockResolvedValue({
      currentPath: 'C:\\Projects\\collab',
      parent: 'C:\\Projects',
      directories: [],
    })

    renderWithQuery(
      <DirectoryBrowser isOpen={true} initialPath="C:\\Projects\\collab" onSelect={onSelect} onCancel={vi.fn()} />,
    )

    await waitFor(() => {
      expect(screen.getByText('No subdirectories')).toBeInTheDocument()
    })

    await user.click(screen.getByText('Select'))
    expect(onSelect).toHaveBeenCalledWith('C:\\Projects\\collab')
  })

  test('calls onCancel when Cancel is clicked', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()

    mockBrowse.mockResolvedValue({
      currentPath: 'C:\\Projects',
      parent: 'C:\\',
      directories: [],
    })

    renderWithQuery(
      <DirectoryBrowser isOpen={true} initialPath="C:\\Projects" onSelect={vi.fn()} onCancel={onCancel} />,
    )

    await user.click(screen.getByText('Cancel'))
    expect(onCancel).toHaveBeenCalledOnce()
  })

  test('shows empty state when directory has no subdirectories', async () => {
    mockBrowse.mockResolvedValue({
      currentPath: 'C:\\empty',
      parent: 'C:\\',
      directories: [],
    })

    renderWithQuery(<DirectoryBrowser isOpen={true} initialPath="C:\\empty" onSelect={vi.fn()} onCancel={vi.fn()} />)

    await waitFor(() => {
      expect(screen.getByText('No subdirectories')).toBeInTheDocument()
    })
  })

  test('shows loading spinner while fetching', () => {
    // Never resolve the promise to keep loading state
    mockBrowse.mockReturnValue(new Promise(() => {}))

    renderWithQuery(<DirectoryBrowser isOpen={true} initialPath="C:\\Projects" onSelect={vi.fn()} onCancel={vi.fn()} />)

    expect(screen.getByLabelText('Loading')).toBeInTheDocument()
  })

  test('up button navigates to parent directory', async () => {
    const user = userEvent.setup()

    mockBrowse
      .mockResolvedValueOnce({
        currentPath: 'C:\\Projects\\collab',
        parent: 'C:\\Projects',
        directories: [],
      })
      .mockResolvedValueOnce({
        currentPath: 'C:\\Projects',
        parent: 'C:\\',
        directories: [{ name: 'collab', path: 'C:\\Projects\\collab' }],
      })

    renderWithQuery(
      <DirectoryBrowser isOpen={true} initialPath="C:\\Projects\\collab" onSelect={vi.fn()} onCancel={vi.fn()} />,
    )

    await waitFor(() => {
      expect(screen.getByText('No subdirectories')).toBeInTheDocument()
    })

    await user.click(screen.getByLabelText('Navigate to parent directory'))

    await waitFor(() => {
      expect(screen.getByText('collab')).toBeInTheDocument()
    })
  })
})

describe('buildBreadcrumbs', () => {
  test('returns empty array for empty path', () => {
    expect(buildBreadcrumbs('')).toEqual([])
  })

  test('parses Windows drive root', () => {
    const crumbs = buildBreadcrumbs('C:\\')
    expect(crumbs).toEqual([{ label: 'C:', path: 'C:\\' }])
  })

  test('parses Windows nested path', () => {
    const crumbs = buildBreadcrumbs('C:\\Projects\\collab')
    expect(crumbs).toEqual([
      { label: 'C:', path: 'C:\\' },
      { label: 'Projects', path: 'C:\\Projects\\' },
      { label: 'collab', path: 'C:\\Projects\\collab\\' },
    ])
  })

  test('parses Unix root', () => {
    const crumbs = buildBreadcrumbs('/')
    expect(crumbs).toEqual([{ label: '/', path: '/' }])
  })

  test('parses Unix nested path', () => {
    const crumbs = buildBreadcrumbs('/home/user/projects')
    expect(crumbs).toEqual([
      { label: '/', path: '/' },
      { label: 'home', path: '/home/' },
      { label: 'user', path: '/home/user/' },
      { label: 'projects', path: '/home/user/projects/' },
    ])
  })
})
