import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('@/hooks/use-app-create', () => ({
  useAppTypes: vi.fn(),
  useRegistrationSchema: vi.fn(),
  useCreateApp: vi.fn(),
}))

vi.mock('@/hooks/use-detect-strategy', () => ({
  useDetectStrategy: vi.fn(),
}))

vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}))

import type { AppTypeListItem, RegistrationSchema } from '@/api/types'
import { useAppTypes, useCreateApp, useRegistrationSchema } from '@/hooks/use-app-create'
import { useDetectStrategy } from '@/hooks/use-detect-strategy'
import { AppCreatePage } from './AppCreatePage'

const mockUseAppTypes = vi.mocked(useAppTypes)
const mockUseRegistrationSchema = vi.mocked(useRegistrationSchema)
const mockUseCreateApp = vi.mocked(useCreateApp)
const mockUseDetectStrategy = vi.mocked(useDetectStrategy)

const appType: AppTypeListItem = {
  slug: 'internal-service',
  displayName: 'Internal Service',
  description: 'A managed process with no route',
  isBuiltIn: true,
}

// A schema with a single number field carrying a non-null default, so the
// cleared-field regression has a value to snap back to (FE-FORM-01).
function makeSchema(): RegistrationSchema {
  return {
    appType: { slug: 'internal-service', displayName: 'Internal Service', description: null },
    sections: [
      {
        key: 'process',
        title: 'Process',
        fields: [
          {
            key: 'startupTimeoutSeconds',
            label: 'Startup Timeout',
            type: 'number',
            required: false,
            defaultValue: 30,
          },
        ],
      },
    ],
  }
}

function makeMutationStub() {
  return {
    mutate: vi.fn(),
    isPending: false,
    isError: false,
    error: null,
    reset: vi.fn(),
  }
}

function setupDefaults() {
  mockUseAppTypes.mockReturnValue({
    data: [appType],
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useAppTypes>)

  mockUseRegistrationSchema.mockReturnValue({
    data: makeSchema(),
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useRegistrationSchema>)

  mockUseCreateApp.mockReturnValue(makeMutationStub() as unknown as ReturnType<typeof useCreateApp>)

  mockUseDetectStrategy.mockReturnValue({
    data: undefined,
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useDetectStrategy>)
}

describe('AppCreatePage — cleared field arms empty (FE-FORM-01, #421)', () => {
  beforeEach(() => {
    setupDefaults()
  })

  test('the number field is seeded from its schema default', async () => {
    const user = userEvent.setup()
    render(<AppCreatePage />)

    // Step 1: pick the type to reach the schema-driven form.
    await user.click(screen.getByText('Internal Service'))

    const input = screen.getByRole('spinbutton') as HTMLInputElement
    expect(input.value).toBe('30')
  })

  test('clearing a number field renders the input empty (does not snap back to the default)', async () => {
    const user = userEvent.setup()
    render(<AppCreatePage />)
    await user.click(screen.getByText('Internal Service'))

    const input = screen.getByRole('spinbutton') as HTMLInputElement
    expect(input.value).toBe('30')

    await user.clear(input)

    // Before the fix, the `?? field.defaultValue` display fallback snapped the
    // cleared field back to "30" while the form value carried null — the operator
    // saw the default but validation would flag the field Required. The fix makes
    // the form state the sole display source once seeded.
    expect(input.value).toBe('')
  })
})
