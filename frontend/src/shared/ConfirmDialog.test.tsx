import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, test, vi } from 'vitest'
import { ConfirmDialog } from './ConfirmDialog'

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

describe('ConfirmDialog', () => {
  test('does not render dialog content when closed', () => {
    render(
      <ConfirmDialog
        title="Delete App"
        message="Are you sure?"
        confirmLabel="Delete"
        isOpen={false}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )
    // Dialog element exists but should not be open
    const dialog = document.querySelector('dialog')
    expect(dialog).toBeInTheDocument()
    expect(dialog).not.toHaveAttribute('open')
  })

  test('shows dialog content when open', () => {
    render(
      <ConfirmDialog
        title="Delete App"
        message="This action cannot be undone."
        confirmLabel="Delete"
        isOpen={true}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )
    expect(screen.getByText('Delete App')).toBeInTheDocument()
    expect(screen.getByText('This action cannot be undone.')).toBeInTheDocument()
    expect(screen.getByText('Delete')).toBeInTheDocument()
    expect(screen.getByText('Cancel')).toBeInTheDocument()
  })

  test('calls onConfirm when confirm button is clicked', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()

    render(
      <ConfirmDialog
        title="Delete App"
        message="Are you sure?"
        confirmLabel="Delete"
        isOpen={true}
        onConfirm={onConfirm}
        onCancel={vi.fn()}
      />,
    )

    await user.click(screen.getByText('Delete'))
    expect(onConfirm).toHaveBeenCalledOnce()
  })

  test('calls onCancel when cancel button is clicked', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()

    render(
      <ConfirmDialog
        title="Delete App"
        message="Are you sure?"
        confirmLabel="Delete"
        isOpen={true}
        onConfirm={vi.fn()}
        onCancel={onCancel}
      />,
    )

    await user.click(screen.getByText('Cancel'))
    expect(onCancel).toHaveBeenCalledOnce()
  })

  test('shows pending state on confirm button', () => {
    render(
      <ConfirmDialog
        title="Delete App"
        message="Are you sure?"
        confirmLabel="Delete"
        isOpen={true}
        isPending={true}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    expect(screen.getByText('Working...')).toBeInTheDocument()
    // Both buttons should be disabled
    const buttons = screen.getAllByRole('button')
    for (const button of buttons) {
      expect(button).toBeDisabled()
    }
  })

  test('opens and closes when isOpen prop changes', () => {
    const { rerender } = render(
      <ConfirmDialog
        title="Delete App"
        message="Are you sure?"
        confirmLabel="Delete"
        isOpen={false}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    const dialog = document.querySelector('dialog')
    expect(dialog).not.toHaveAttribute('open')

    rerender(
      <ConfirmDialog
        title="Delete App"
        message="Are you sure?"
        confirmLabel="Delete"
        isOpen={true}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    expect(dialog).toHaveAttribute('open')

    rerender(
      <ConfirmDialog
        title="Delete App"
        message="Are you sure?"
        confirmLabel="Delete"
        isOpen={false}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    expect(dialog).not.toHaveAttribute('open')
  })

  test('uses danger variant by default on confirm button', () => {
    render(
      <ConfirmDialog
        title="Delete App"
        message="Are you sure?"
        confirmLabel="Delete"
        isOpen={true}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    const confirmButton = screen.getByText('Delete')
    expect(confirmButton).toHaveClass('wm-btn--danger')
  })

  test('uses primary variant when specified', () => {
    render(
      <ConfirmDialog
        title="Save Changes"
        message="Save your changes?"
        confirmLabel="Save"
        confirmVariant="primary"
        isOpen={true}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    const confirmButton = screen.getByText('Save')
    expect(confirmButton).toHaveClass('wm-btn--primary')
  })
})
