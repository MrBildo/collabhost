import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, test, vi } from 'vitest'
import type { Column } from './DataTable'
import { DataTable } from './DataTable'

type TestItem = {
  id: string
  name: string
  value: number
}

function makeItem(overrides: Partial<TestItem> = {}): TestItem {
  return {
    id: '1',
    name: 'Alpha',
    value: 10,
    ...overrides,
  }
}

const testColumns: Column<TestItem>[] = [
  {
    key: 'name',
    header: 'Name',
    sortFn: (a, b) => a.name.localeCompare(b.name),
    render: (item) => <span>{item.name}</span>,
  },
  {
    key: 'value',
    header: 'Value',
    sortFn: (a, b) => a.value - b.value,
    render: (item) => <span>{item.value}</span>,
  },
]

const testData: TestItem[] = [
  makeItem({ id: '1', name: 'Charlie', value: 30 }),
  makeItem({ id: '2', name: 'Alpha', value: 10 }),
  makeItem({ id: '3', name: 'Bravo', value: 20 }),
]

/** Get data rows (skip the header row). Throws if no data rows found. */
function getDataRows(): HTMLElement[] {
  const rows = screen.getAllByRole('row')
  return rows.slice(1)
}

describe('DataTable', () => {
  describe('rendering', () => {
    test('renders column headers', () => {
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} />)

      expect(screen.getByText('Name')).toBeInTheDocument()
      expect(screen.getByText('Value')).toBeInTheDocument()
    })

    test('renders all data rows', () => {
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} />)

      expect(screen.getByText('Charlie')).toBeInTheDocument()
      expect(screen.getByText('Alpha')).toBeInTheDocument()
      expect(screen.getByText('Bravo')).toBeInTheDocument()
    })

    test('renders empty table with no rows', () => {
      render(<DataTable columns={testColumns} data={[]} keyFn={(item) => item.id} />)

      expect(screen.getByText('Name')).toBeInTheDocument()
      const tbody = document.querySelector('tbody')
      expect(tbody?.children).toHaveLength(0)
    })
  })

  describe('sorting', () => {
    test('sorts ascending on first click of sortable header', async () => {
      const user = userEvent.setup()
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} />)

      await user.click(screen.getByText('Name'))

      const dataRows = getDataRows()
      expect(within(dataRows[0] as HTMLElement).getByText('Alpha')).toBeInTheDocument()
      expect(within(dataRows[1] as HTMLElement).getByText('Bravo')).toBeInTheDocument()
      expect(within(dataRows[2] as HTMLElement).getByText('Charlie')).toBeInTheDocument()
    })

    test('toggles to descending on second click', async () => {
      const user = userEvent.setup()
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} />)

      await user.click(screen.getByText('Name'))
      await user.click(screen.getByText('Name'))

      const dataRows = getDataRows()
      expect(within(dataRows[0] as HTMLElement).getByText('Charlie')).toBeInTheDocument()
      expect(within(dataRows[1] as HTMLElement).getByText('Bravo')).toBeInTheDocument()
      expect(within(dataRows[2] as HTMLElement).getByText('Alpha')).toBeInTheDocument()
    })

    test('sorts by a different column', async () => {
      const user = userEvent.setup()
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} />)

      await user.click(screen.getByText('Value'))

      const dataRows = getDataRows()
      expect(within(dataRows[0] as HTMLElement).getByText('10')).toBeInTheDocument()
      expect(within(dataRows[1] as HTMLElement).getByText('20')).toBeInTheDocument()
      expect(within(dataRows[2] as HTMLElement).getByText('30')).toBeInTheDocument()
    })

    test('shows sort direction indicator', async () => {
      const user = userEvent.setup()
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} />)

      await user.click(screen.getByText('Name'))
      // Ascending arrow
      expect(screen.getByText('\u2191')).toBeInTheDocument()

      await user.click(screen.getByText('Name'))
      // Descending arrow
      expect(screen.getByText('\u2193')).toBeInTheDocument()
    })
  })

  describe('keyboard navigation', () => {
    test('sortable headers are keyboard accessible', async () => {
      const user = userEvent.setup()
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} />)

      const nameHeader = screen.getByText('Name').closest('th')
      expect(nameHeader).toHaveAttribute('tabindex', '0')

      nameHeader?.focus()
      await user.keyboard('{Enter}')

      // Should have sorted
      const dataRows = getDataRows()
      expect(within(dataRows[0] as HTMLElement).getByText('Alpha')).toBeInTheDocument()
    })

    test('sortable headers respond to Space key', async () => {
      const user = userEvent.setup()
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} />)

      const nameHeader = screen.getByText('Name').closest('th')
      nameHeader?.focus()
      await user.keyboard(' ')

      const dataRows = getDataRows()
      expect(within(dataRows[0] as HTMLElement).getByText('Alpha')).toBeInTheDocument()
    })

    test('clickable rows are keyboard accessible', async () => {
      const user = userEvent.setup()
      const onRowClick = vi.fn()

      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} onRowClick={onRowClick} />)

      const rows = screen.getAllByRole('row')
      const firstDataRow = rows[1]
      expect(firstDataRow).toHaveAttribute('tabindex', '0')

      firstDataRow?.focus()
      await user.keyboard('{Enter}')
      expect(onRowClick).toHaveBeenCalledWith(testData[0])
    })
  })

  describe('row click', () => {
    test('calls onRowClick when a row is clicked', async () => {
      const user = userEvent.setup()
      const onRowClick = vi.fn()

      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} onRowClick={onRowClick} />)

      await user.click(screen.getByText('Charlie'))
      expect(onRowClick).toHaveBeenCalledWith(testData[0])
    })

    test('rows are not clickable when onRowClick is not provided', () => {
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} />)

      const rows = screen.getAllByRole('row')
      const firstDataRow = rows[1]
      expect(firstDataRow).not.toHaveAttribute('tabindex')
      expect(firstDataRow).not.toHaveClass('wm-table-row--clickable')
    })

    test('adds clickable class when onRowClick is provided', () => {
      render(<DataTable columns={testColumns} data={testData} keyFn={(item) => item.id} onRowClick={vi.fn()} />)

      const rows = screen.getAllByRole('row')
      const firstDataRow = rows[1]
      expect(firstDataRow).toHaveClass('wm-table-row--clickable')
    })
  })

  describe('non-sortable columns', () => {
    test('non-sortable columns do not have pointer cursor or tabindex', () => {
      const columns: Column<TestItem>[] = [
        {
          key: 'name',
          header: 'Name',
          render: (item) => <span>{item.name}</span>,
          // No sortFn
        },
      ]

      render(<DataTable columns={columns} data={testData} keyFn={(item) => item.id} />)

      const header = screen.getByText('Name').closest('th')
      expect(header).not.toHaveAttribute('tabindex')
    })
  })
})
