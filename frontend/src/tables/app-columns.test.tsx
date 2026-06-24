import type { AppListItem } from '@/api/types'
import { render, screen } from '@testing-library/react'
import { describe, expect, test, vi } from 'vitest'
import { buildDashboardColumns } from './app-columns'

function makeApp(overrides: Partial<AppListItem> = {}): AppListItem {
  return {
    id: 'app-1',
    name: 'my-api',
    displayName: 'My API',
    appType: { slug: 'dotnet-app', displayName: '.NET App' },
    status: 'stopped',
    domain: null,
    domainActive: false,
    scheme: 'https',
    port: null,
    uptimeSeconds: null,
    actions: { canStart: true, canStop: true },
    ...overrides,
  }
}

function renderDomainCell(app: AppListItem) {
  const columns = buildDashboardColumns({ onStart: vi.fn(), onStop: vi.fn(), pendingSlug: null })
  const domainCol = columns.find((c) => c.key === 'domain')
  if (!domainCol) throw new Error('domain column not found')
  return render(
    <table>
      <tbody>
        <tr>
          <td>{domainCol.render(app)}</td>
        </tr>
      </tbody>
    </table>,
  )
}

function renderActionsCell(app: AppListItem, pendingSlug: string | null) {
  const columns = buildDashboardColumns({ onStart: vi.fn(), onStop: vi.fn(), pendingSlug })
  const actionsCol = columns.find((c) => c.key === 'actions')
  if (!actionsCol) throw new Error('actions column not found')
  return render(
    <table>
      <tbody>
        <tr>
          <td>{actionsCol.render(app)}</td>
        </tr>
      </tbody>
    </table>,
  )
}

describe('app-columns actionsColumn pending state (FE-UI-08)', () => {
  test('a row whose own action is pending has disabled buttons', () => {
    renderActionsCell(makeApp({ name: 'my-api' }), 'my-api')

    expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeDisabled()
  })

  test('a row whose action is NOT pending stays enabled while another row transitions', () => {
    // 'other-app' is mid-action; this row ('my-api') must NOT be disabled.
    renderActionsCell(makeApp({ name: 'my-api' }), 'other-app')

    expect(screen.getByRole('button', { name: 'Start' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeEnabled()
  })

  test('all rows enabled when nothing is pending', () => {
    renderActionsCell(makeApp({ name: 'my-api' }), null)

    expect(screen.getByRole('button', { name: 'Start' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeEnabled()
  })
})

describe('app-columns domainColumn scheme (FE-UI-05 / #438)', () => {
  test('active-domain link uses the backend scheme — https', () => {
    renderDomainCell(makeApp({ domain: 'my-api.collab.internal', domainActive: true, scheme: 'https' }))

    const link = screen.getByRole('link', { name: 'my-api.collab.internal' })
    expect(link).toHaveAttribute('href', 'https://my-api.collab.internal')
  })

  test('active-domain link uses the backend scheme — http (no hardcoded https)', () => {
    renderDomainCell(makeApp({ domain: 'my-api.collab.internal', domainActive: true, scheme: 'http' }))

    const link = screen.getByRole('link', { name: 'my-api.collab.internal' })
    expect(link).toHaveAttribute('href', 'http://my-api.collab.internal')
  })
})
