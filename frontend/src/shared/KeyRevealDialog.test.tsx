import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, test, vi } from 'vitest'
import { KeyRevealDialog } from './KeyRevealDialog'

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

function makeUser() {
  return {
    id: 'user-1',
    name: 'CI Agent',
    role: 'agent' as const,
    isActive: true,
    createdAt: '2026-04-06T00:00:00Z',
    authKey: '01JRNXXXXXXXXXXXXXXXXXXXXXX',
  }
}

function mockClipboard() {
  const writeText = vi.fn().mockResolvedValue(undefined)
  Object.defineProperty(navigator, 'clipboard', {
    value: { writeText },
    writable: true,
    configurable: true,
  })
  return writeText
}

describe('KeyRevealDialog', () => {
  test('renders user name and role', () => {
    render(<KeyRevealDialog user={makeUser()} onDone={vi.fn()} />)
    expect(screen.getByText('CI Agent')).toBeInTheDocument()
    expect(screen.getByText('Agent')).toBeInTheDocument()
  })

  test('displays the auth key', () => {
    render(<KeyRevealDialog user={makeUser()} onDone={vi.fn()} />)
    expect(screen.getByText('01JRNXXXXXXXXXXXXXXXXXXXXXX')).toBeInTheDocument()
  })

  test('shows Done button as disabled initially', () => {
    render(<KeyRevealDialog user={makeUser()} onDone={vi.fn()} />)
    expect(screen.getByRole('button', { name: /Done/i })).toBeDisabled()
  })

  test('Done button enables after acknowledging checkbox', async () => {
    const user = userEvent.setup()
    render(<KeyRevealDialog user={makeUser()} onDone={vi.fn()} />)

    const checkbox = screen.getByRole('checkbox')
    await user.click(checkbox)

    expect(screen.getByRole('button', { name: /Done/i })).not.toBeDisabled()
  })

  test('calls onDone when Done is clicked after acknowledgment', async () => {
    const user = userEvent.setup()
    const onDone = vi.fn()
    render(<KeyRevealDialog user={makeUser()} onDone={onDone} />)

    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: /Done/i }))

    expect(onDone).toHaveBeenCalledOnce()
  })

  test('copy button copies key to clipboard', async () => {
    const user = userEvent.setup()
    const writeText = mockClipboard()

    render(<KeyRevealDialog user={makeUser()} onDone={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: /Copy/i }))

    expect(writeText).toHaveBeenCalledWith('01JRNXXXXXXXXXXXXXXXXXXXXXX')
  })

  test('copy button shows Copied feedback after click', async () => {
    const user = userEvent.setup()
    mockClipboard()

    render(<KeyRevealDialog user={makeUser()} onDone={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: /Copy/i }))

    expect(screen.getByRole('button', { name: /Copied/i })).toBeInTheDocument()
  })

  test('dialog blocks Escape dismissal', async () => {
    const user = userEvent.setup()
    const onDone = vi.fn()
    render(<KeyRevealDialog user={makeUser()} onDone={onDone} />)

    await user.keyboard('{Escape}')

    // onDone should not have been called — dialog should still be open
    expect(onDone).not.toHaveBeenCalled()
  })

  test('shows key-not-shown-again warning', () => {
    render(<KeyRevealDialog user={makeUser()} onDone={vi.fn()} />)
    expect(screen.getByText(/This key will not be shown again/i)).toBeInTheDocument()
  })
})
