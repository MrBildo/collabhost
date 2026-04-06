import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, test, vi } from 'vitest'
import { RestartConfirmDialog } from './RestartConfirmDialog'

// jsdom does not implement showModal/close on HTMLDialogElement.
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

describe('RestartConfirmDialog', () => {
  test('does not show dialog content when closed', () => {
    render(
      <RestartConfirmDialog
        isOpen={false}
        isPending={false}
        onSaveAndRestart={vi.fn()}
        onSaveOnly={vi.fn()}
        onCancel={vi.fn()}
      />,
    )
    const dialog = document.querySelector('dialog')
    expect(dialog).toBeInTheDocument()
    expect(dialog).not.toHaveAttribute('open')
  })

  test('shows dialog content when open', () => {
    render(
      <RestartConfirmDialog
        isOpen={true}
        isPending={false}
        onSaveAndRestart={vi.fn()}
        onSaveOnly={vi.fn()}
        onCancel={vi.fn()}
      />,
    )
    expect(screen.getByText('Restart Required')).toBeInTheDocument()
    expect(screen.getByText('Cancel')).toBeInTheDocument()
    expect(screen.getByText('Save Only')).toBeInTheDocument()
    expect(screen.getByText('Save & Restart')).toBeInTheDocument()
  })

  test('calls onSaveAndRestart when Save & Restart is clicked', async () => {
    const user = userEvent.setup()
    const onSaveAndRestart = vi.fn()

    render(
      <RestartConfirmDialog
        isOpen={true}
        isPending={false}
        onSaveAndRestart={onSaveAndRestart}
        onSaveOnly={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    await user.click(screen.getByText('Save & Restart'))
    expect(onSaveAndRestart).toHaveBeenCalledOnce()
  })

  test('calls onSaveOnly when Save Only is clicked', async () => {
    const user = userEvent.setup()
    const onSaveOnly = vi.fn()

    render(
      <RestartConfirmDialog
        isOpen={true}
        isPending={false}
        onSaveAndRestart={vi.fn()}
        onSaveOnly={onSaveOnly}
        onCancel={vi.fn()}
      />,
    )

    await user.click(screen.getByText('Save Only'))
    expect(onSaveOnly).toHaveBeenCalledOnce()
  })

  test('calls onCancel when Cancel is clicked', async () => {
    const user = userEvent.setup()
    const onCancel = vi.fn()

    render(
      <RestartConfirmDialog
        isOpen={true}
        isPending={false}
        onSaveAndRestart={vi.fn()}
        onSaveOnly={vi.fn()}
        onCancel={onCancel}
      />,
    )

    await user.click(screen.getByText('Cancel'))
    expect(onCancel).toHaveBeenCalledOnce()
  })

  test('disables all buttons when pending', () => {
    render(
      <RestartConfirmDialog
        isOpen={true}
        isPending={true}
        onSaveAndRestart={vi.fn()}
        onSaveOnly={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    const buttons = screen.getAllByRole('button')
    for (const button of buttons) {
      expect(button).toBeDisabled()
    }
  })

  test('shows Saving... text on action buttons when pending', () => {
    render(
      <RestartConfirmDialog
        isOpen={true}
        isPending={true}
        onSaveAndRestart={vi.fn()}
        onSaveOnly={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    const savingButtons = screen.getAllByText('Saving...')
    expect(savingButtons).toHaveLength(2)
  })

  test('opens and closes when isOpen prop changes', () => {
    const { rerender } = render(
      <RestartConfirmDialog
        isOpen={false}
        isPending={false}
        onSaveAndRestart={vi.fn()}
        onSaveOnly={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    const dialog = document.querySelector('dialog')
    expect(dialog).not.toHaveAttribute('open')

    rerender(
      <RestartConfirmDialog
        isOpen={true}
        isPending={false}
        onSaveAndRestart={vi.fn()}
        onSaveOnly={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    expect(dialog).toHaveAttribute('open')

    rerender(
      <RestartConfirmDialog
        isOpen={false}
        isPending={false}
        onSaveAndRestart={vi.fn()}
        onSaveOnly={vi.fn()}
        onCancel={vi.fn()}
      />,
    )

    expect(dialog).not.toHaveAttribute('open')
  })
})
