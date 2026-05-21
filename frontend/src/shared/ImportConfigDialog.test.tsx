import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, test, vi } from 'vitest'
import { ImportConfigDialog } from './ImportConfigDialog'

// jsdom does not implement showModal/close on HTMLDialogElement.
// Polyfill them so the component's useEffect can call them.
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
})

function defaults() {
  return {
    isOpen: true,
    isPending: false,
    imported: null as Record<string, string> | null,
    skipped: [] as string[],
    sourcePath: null as string | null,
    error: null as string | null,
    onConfirm: vi.fn(),
    onCancel: vi.fn(),
  }
}

describe('ImportConfigDialog', () => {
  test('renders title and a loading body when the preview has not landed yet', () => {
    render(<ImportConfigDialog {...defaults()} />)
    expect(screen.getByText('Import current config.json')).toBeInTheDocument()
    expect(screen.getByText('Loading preview...')).toBeInTheDocument()
  })

  test('renders imported entries when the preview lands', () => {
    const props = {
      ...defaults(),
      imported: { 'api-base-url': 'https://api.example.com', 'feature-flag-a': 'true' },
      sourcePath: '/srv/app/config.json',
    }
    render(<ImportConfigDialog {...props} />)
    expect(screen.getByText('api-base-url')).toBeInTheDocument()
    expect(screen.getByText('https://api.example.com')).toBeInTheDocument()
    expect(screen.getByText('feature-flag-a')).toBeInTheDocument()
    expect(screen.getByText('/srv/app/config.json')).toBeInTheDocument()
  })

  test('renders skipped entries with a warning', () => {
    const props = {
      ...defaults(),
      imported: { 'api-base-url': 'https://api.example.com' },
      skipped: ['nested', 'non-string', 'null-key'],
    }
    render(<ImportConfigDialog {...props} />)
    expect(screen.getByText('nested')).toBeInTheDocument()
    expect(screen.getByText('non-string')).toBeInTheDocument()
    expect(screen.getByText('null-key')).toBeInTheDocument()
    expect(screen.getByText(/Skipped 3 non-flat entries/)).toBeInTheDocument()
  })

  test('renders a message when the file has no flat entries', () => {
    const props = {
      ...defaults(),
      imported: {},
      skipped: [],
      sourcePath: '/srv/app/config.json',
    }
    render(<ImportConfigDialog {...props} />)
    expect(screen.getByText(/No flat string entries were found/)).toBeInTheDocument()
  })

  test('renders the error and disables Apply when the preview call fails', () => {
    const props = {
      ...defaults(),
      error: "No file found at '/srv/app/config.json' to import for app 'uat-336'.",
    }
    render(<ImportConfigDialog {...props} />)
    expect(screen.getByText(/No file found at/)).toBeInTheDocument()
    expect(screen.getByText('Apply')).toBeDisabled()
  })

  test('Apply stays disabled until a non-empty preview is loaded', () => {
    const { rerender } = render(<ImportConfigDialog {...defaults()} />)
    expect(screen.getByText('Apply')).toBeDisabled()

    // Empty preview is also a disabled state — nothing to merge in.
    rerender(<ImportConfigDialog {...defaults()} imported={{}} />)
    expect(screen.getByText('Apply')).toBeDisabled()

    rerender(<ImportConfigDialog {...defaults()} imported={{ 'api-base-url': 'x' }} />)
    expect(screen.getByText('Apply')).not.toBeDisabled()
  })

  test('calls onConfirm when Apply is clicked', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    render(<ImportConfigDialog {...defaults()} imported={{ 'api-base-url': 'x' }} onConfirm={onConfirm} />)
    await user.click(screen.getByText('Apply'))
    expect(onConfirm).toHaveBeenCalledOnce()
  })

  test('calls onCancel when Cancel is clicked', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()
    render(<ImportConfigDialog {...defaults()} onCancel={onCancel} />)
    await user.click(screen.getByText('Cancel'))
    expect(onCancel).toHaveBeenCalledOnce()
  })

  test('shows pending state on Apply button', () => {
    const props = {
      ...defaults(),
      isPending: true,
      imported: { 'api-base-url': 'x' },
    }
    render(<ImportConfigDialog {...props} />)
    expect(screen.getByText('Importing...')).toBeInTheDocument()
    const buttons = screen.getAllByRole('button')
    for (const button of buttons) {
      expect(button).toBeDisabled()
    }
  })

  test('opens and closes when isOpen prop changes', () => {
    const { rerender } = render(<ImportConfigDialog {...defaults()} isOpen={false} />)
    const dialog = document.querySelector('dialog')
    expect(dialog).not.toHaveAttribute('open')

    rerender(<ImportConfigDialog {...defaults()} isOpen={true} />)
    expect(dialog).toHaveAttribute('open')

    rerender(<ImportConfigDialog {...defaults()} isOpen={false} />)
    expect(dialog).not.toHaveAttribute('open')
  })
})
