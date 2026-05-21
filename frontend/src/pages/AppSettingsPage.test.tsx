import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('@/hooks/use-app-settings', () => ({
  useAppSettings: vi.fn(),
  useSaveSettings: vi.fn(),
  useDeleteApp: vi.fn(),
  useImportRuntimeConfigFile: vi.fn(),
  useSettingsRestartApp: vi.fn(),
}))

vi.mock('@/hooks/use-current-user', () => ({
  useCurrentUser: vi.fn(),
}))

vi.mock('react-router-dom', () => ({
  useParams: () => ({ slug: 'uat-336' }),
  useNavigate: () => vi.fn(),
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}))

import type { AppSettings, RuntimeConfigFileImportResponse } from '@/api/types'
import {
  useAppSettings,
  useDeleteApp,
  useImportRuntimeConfigFile,
  useSaveSettings,
  useSettingsRestartApp,
} from '@/hooks/use-app-settings'
import { useCurrentUser } from '@/hooks/use-current-user'
import { AppSettingsPage } from './AppSettingsPage'

const mockUseAppSettings = vi.mocked(useAppSettings)
const mockUseSaveSettings = vi.mocked(useSaveSettings)
const mockUseDeleteApp = vi.mocked(useDeleteApp)
const mockUseImportRuntimeConfigFile = vi.mocked(useImportRuntimeConfigFile)
const mockUseSettingsRestartApp = vi.mocked(useSettingsRestartApp)
const mockUseCurrentUser = vi.mocked(useCurrentUser)

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

function makeMutationStub<TData = unknown, TVars = unknown>(
  mutateImpl?: (vars: TVars, callbacks?: { onSuccess?: (data: TData) => void; onError?: (e: Error) => void }) => void,
) {
  return {
    mutate: vi.fn(mutateImpl ?? (() => undefined)) as ReturnType<typeof vi.fn>,
    isPending: false,
    isError: false,
    error: null,
    reset: vi.fn(),
  }
}

function makeSettings(overrides: Partial<AppSettings> = {}): AppSettings {
  return {
    id: '01ULID0000000000000000000A',
    name: 'uat-336',
    displayName: 'UAT 336',
    appTypeName: 'Static Site',
    registeredAt: '2026-04-01T00:00:00Z',
    sections: [
      {
        key: 'runtime-config-file',
        title: 'Runtime Config File',
        fields: [
          {
            key: 'path',
            label: 'File Path',
            type: 'text',
            value: '/config.json',
            defaultValue: '/config.json',
            editable: { mode: 'locked', reason: 'Set during registration' },
            requiresRestart: false,
          },
          {
            key: 'values',
            label: 'Values',
            type: 'keyvalue',
            value: {},
            defaultValue: {},
            editable: { mode: 'always' },
            requiresRestart: false,
            keyPattern: '^[^\\s]+$',
            keyPatternMessage: 'Keys must be non-empty and contain no whitespace.',
          },
        ],
      },
    ],
    ...overrides,
  }
}

function setupDefaults() {
  mockUseAppSettings.mockReturnValue({
    data: makeSettings(),
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useAppSettings>)

  mockUseSaveSettings.mockReturnValue(makeMutationStub() as unknown as ReturnType<typeof useSaveSettings>)
  mockUseDeleteApp.mockReturnValue(makeMutationStub() as unknown as ReturnType<typeof useDeleteApp>)
  mockUseSettingsRestartApp.mockReturnValue(makeMutationStub() as unknown as ReturnType<typeof useSettingsRestartApp>)
  mockUseImportRuntimeConfigFile.mockReturnValue(
    makeMutationStub() as unknown as ReturnType<typeof useImportRuntimeConfigFile>,
  )

  mockUseCurrentUser.mockReturnValue({
    data: { id: 'u1', name: 'Admin', role: 'administrator', isActive: true, createdAt: '' },
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useCurrentUser>)
}

describe('AppSettingsPage — runtime-config-file import (Card #336)', () => {
  beforeEach(() => {
    setupDefaults()
  })

  test('import button is not shown in read mode', () => {
    render(<AppSettingsPage />)
    expect(screen.queryByRole('button', { name: /Import current config\.json/i })).toBeNull()
  })

  test('import button appears in edit mode only for the runtime-config-file section', async () => {
    const user = userEvent.setup()
    render(<AppSettingsPage />)
    await user.click(screen.getByRole('button', { name: 'Edit' }))
    expect(screen.getByRole('button', { name: /Import current config\.json/i })).toBeInTheDocument()
  })

  test('import button is not shown for an app type without the runtime-config-file capability', async () => {
    mockUseAppSettings.mockReturnValue({
      data: makeSettings({
        sections: [
          {
            key: 'identity',
            title: 'Identity',
            fields: [
              {
                key: 'displayName',
                label: 'Display Name',
                type: 'text',
                value: 'X',
                defaultValue: 'X',
                editable: { mode: 'always' },
              },
            ],
          },
        ],
      }),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useAppSettings>)

    const user = userEvent.setup()
    render(<AppSettingsPage />)
    await user.click(screen.getByRole('button', { name: 'Edit' }))
    expect(screen.queryByRole('button', { name: /Import current config\.json/i })).toBeNull()
  })

  test('clicking import opens the dialog and triggers the preview call', async () => {
    let onSuccess: ((data: RuntimeConfigFileImportResponse) => void) | undefined
    const importStub = makeMutationStub<RuntimeConfigFileImportResponse, void>((_vars, callbacks) => {
      onSuccess = callbacks?.onSuccess
    })
    mockUseImportRuntimeConfigFile.mockReturnValue(
      importStub as unknown as ReturnType<typeof useImportRuntimeConfigFile>,
    )

    const user = userEvent.setup()
    render(<AppSettingsPage />)
    await user.click(screen.getByRole('button', { name: 'Edit' }))
    await user.click(screen.getByRole('button', { name: /Import current config\.json/i }))

    expect(importStub.mutate).toHaveBeenCalledOnce()
    expect(screen.getByText('Loading preview...')).toBeInTheDocument()

    // Simulate the preview landing.
    act(() => {
      onSuccess?.({
        imported: { 'api-base-url': 'https://api.example.com' },
        skipped: ['nested'],
        sourcePath: '/srv/uat-336/config.json',
      })
    })

    await waitFor(() => {
      expect(screen.getByText('api-base-url')).toBeInTheDocument()
    })
    expect(screen.getByText('https://api.example.com')).toBeInTheDocument()
    expect(screen.getByText('nested')).toBeInTheDocument()
  })

  test('Apply merges the preview into the values editor', async () => {
    let onSuccess: ((data: RuntimeConfigFileImportResponse) => void) | undefined
    const importStub = makeMutationStub<RuntimeConfigFileImportResponse, void>((_vars, callbacks) => {
      onSuccess = callbacks?.onSuccess
    })
    mockUseImportRuntimeConfigFile.mockReturnValue(
      importStub as unknown as ReturnType<typeof useImportRuntimeConfigFile>,
    )

    const user = userEvent.setup()
    render(<AppSettingsPage />)
    await user.click(screen.getByRole('button', { name: 'Edit' }))
    await user.click(screen.getByRole('button', { name: /Import current config\.json/i }))

    act(() => {
      onSuccess?.({
        imported: { 'api-base-url': 'https://api.example.com' },
        skipped: [],
        sourcePath: '/srv/uat-336/config.json',
      })
    })

    await waitFor(() => expect(screen.getByText('Apply')).not.toBeDisabled())
    await user.click(screen.getByText('Apply'))

    // After Apply, the dialog closes and the KeyValueField in the runtime-config-file
    // section shows the merged entry (the key renders as an input value in edit mode).
    const dialog = document.querySelector('dialog')
    expect(dialog).not.toHaveAttribute('open')
    await waitFor(() => {
      const input = screen.getByLabelText('Key for api-base-url') as HTMLInputElement
      expect(input.value).toBe('api-base-url')
    })
  })

  test('Cancel closes the dialog without changing edit values', async () => {
    let onSuccess: ((data: RuntimeConfigFileImportResponse) => void) | undefined
    const importStub = makeMutationStub<RuntimeConfigFileImportResponse, void>((_vars, callbacks) => {
      onSuccess = callbacks?.onSuccess
    })
    mockUseImportRuntimeConfigFile.mockReturnValue(
      importStub as unknown as ReturnType<typeof useImportRuntimeConfigFile>,
    )

    const user = userEvent.setup()
    render(<AppSettingsPage />)
    await user.click(screen.getByRole('button', { name: 'Edit' }))
    await user.click(screen.getByRole('button', { name: /Import current config\.json/i }))

    act(() => {
      onSuccess?.({
        imported: { 'api-base-url': 'https://api.example.com' },
        skipped: [],
        sourcePath: '/srv/uat-336/config.json',
      })
    })

    await waitFor(() => expect(screen.getByText('Apply')).not.toBeDisabled())
    // Two "Cancel" buttons live on the page in edit mode (the breadcrumb-bar
    // Cancel that exits edit mode, and the dialog's Cancel). We want the
    // dialog one — scope by ancestor.
    const dialogEl = document.querySelector('dialog')
    expect(dialogEl).not.toBeNull()
    if (!dialogEl) throw new Error('dialog missing')
    const cancelInDialog = Array.from(dialogEl.querySelectorAll('button')).find((b) => b.textContent === 'Cancel')
    expect(cancelInDialog).toBeDefined()
    if (!cancelInDialog) throw new Error('dialog Cancel missing')
    await user.click(cancelInDialog)

    // No merged key in the editor — the original empty values stand.
    expect(screen.queryByLabelText('Key for api-base-url')).toBeNull()
  })

  test('renders the importer error message when the preview call fails', async () => {
    let onError: ((e: Error) => void) | undefined
    const importStub = makeMutationStub<RuntimeConfigFileImportResponse, void>((_vars, callbacks) => {
      onError = callbacks?.onError
    })
    mockUseImportRuntimeConfigFile.mockReturnValue(
      importStub as unknown as ReturnType<typeof useImportRuntimeConfigFile>,
    )

    const user = userEvent.setup()
    render(<AppSettingsPage />)
    await user.click(screen.getByRole('button', { name: 'Edit' }))
    await user.click(screen.getByRole('button', { name: /Import current config\.json/i }))

    act(() => {
      onError?.(new Error('Network down'))
    })

    await waitFor(() => expect(screen.getByText('Network down')).toBeInTheDocument())
    expect(screen.getByText('Apply')).toBeDisabled()
  })
})
