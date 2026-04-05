import { cn } from '@/lib/cn'
import { useState } from 'react'
import type { ReactNode } from 'react'

type SortDirection = 'asc' | 'desc'

type Column<T> = {
  key: string
  header: string
  render: (item: T) => ReactNode
  sortFn?: (a: T, b: T) => number
  width?: string
  align?: 'left' | 'right'
}

type DataTableProps<T> = {
  columns: Column<T>[]
  data: T[]
  keyFn: (item: T) => string
  onRowClick?: (item: T) => void
  rowClassName?: (item: T) => string | undefined
  className?: string
}

function DataTable<T>({ columns, data, keyFn, onRowClick, rowClassName, className }: DataTableProps<T>) {
  const [sortKey, setSortKey] = useState<string | null>(null)
  const [sortDir, setSortDir] = useState<SortDirection>('asc')

  function handleSort(key: string): void {
    if (sortKey === key) {
      setSortDir(sortDir === 'asc' ? 'desc' : 'asc')
    } else {
      setSortKey(key)
      setSortDir('asc')
    }
  }

  const sortedData = (() => {
    if (!sortKey) return data
    const col = columns.find((c) => c.key === sortKey)
    if (!col?.sortFn) return data
    const sorted = [...data].sort(col.sortFn)
    return sortDir === 'desc' ? sorted.reverse() : sorted
  })()

  return (
    <div className={cn('wm-panel overflow-hidden', className)}>
      <table className="wm-table">
        <thead>
          <tr>
            {columns.map((col) => (
              <th
                key={col.key}
                style={{
                  width: col.width,
                  textAlign: col.align ?? 'left',
                  cursor: col.sortFn ? 'pointer' : undefined,
                }}
                onClick={col.sortFn ? () => handleSort(col.key) : undefined}
                onKeyDown={
                  col.sortFn
                    ? (e) => {
                        if (e.key === 'Enter' || e.key === ' ') handleSort(col.key)
                      }
                    : undefined
                }
                tabIndex={col.sortFn ? 0 : undefined}
              >
                {col.header}
                {sortKey === col.key && <span className="ml-1">{sortDir === 'asc' ? '\u2191' : '\u2193'}</span>}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sortedData.map((item) => (
            <tr
              key={keyFn(item)}
              className={cn(onRowClick && 'wm-table-row--clickable', rowClassName?.(item))}
              onClick={onRowClick ? () => onRowClick(item) : undefined}
              onKeyDown={
                onRowClick
                  ? (e) => {
                      if (e.key === 'Enter') onRowClick(item)
                    }
                  : undefined
              }
              tabIndex={onRowClick ? 0 : undefined}
            >
              {columns.map((col) => (
                <td key={col.key} style={{ textAlign: col.align ?? 'left' }}>
                  {col.render(item)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export { DataTable }
export type { DataTableProps, Column, SortDirection }
