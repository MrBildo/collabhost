import type { StaticSiteProbe } from '@/api/types'
import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { StaticSitePanel } from './StaticSitePanel'

function makeData(overrides: Partial<StaticSiteProbe> = {}): StaticSiteProbe {
  return {
    hasIndexHtml: true,
    htmlFileCount: 12,
    totalAssetBytes: 1024 * 512, // 512 KB
    hasNestedAssets: true,
    ...overrides,
  }
}

describe('StaticSitePanel', () => {
  test('renders index.html badge when present', () => {
    render(<StaticSitePanel data={makeData({ hasIndexHtml: true })} />)
    expect(screen.getByText('Index')).toBeInTheDocument()
    expect(screen.getByText('index.html')).toBeInTheDocument()
  })

  test('renders None when index.html is absent', () => {
    render(<StaticSitePanel data={makeData({ hasIndexHtml: false })} />)
    expect(screen.getByText('None')).toBeInTheDocument()
  })

  test('renders HTML file count', () => {
    render(<StaticSitePanel data={makeData({ htmlFileCount: 42 })} />)
    expect(screen.getByText('HTML Files')).toBeInTheDocument()
    expect(screen.getByText('42')).toBeInTheDocument()
  })

  test('renders asset size in human format', () => {
    render(<StaticSitePanel data={makeData({ totalAssetBytes: 1024 * 1024 * 5 })} />)
    expect(screen.getByText('Asset Size')).toBeInTheDocument()
    expect(screen.getByText('5.0 MB')).toBeInTheDocument()
  })

  test('renders 200 MB+ when at the backend cap', () => {
    render(<StaticSitePanel data={makeData({ totalAssetBytes: 200 * 1024 * 1024 })} />)
    expect(screen.getByText('200 MB+')).toBeInTheDocument()
  })

  test('renders 200 MB+ when above the backend cap', () => {
    // Defensive: backend should clamp, but we render gracefully if a larger
    // value somehow reaches the panel.
    render(<StaticSitePanel data={makeData({ totalAssetBytes: 300 * 1024 * 1024 })} />)
    expect(screen.getByText('200 MB+')).toBeInTheDocument()
  })

  test('renders Nested layout badge when nested assets detected', () => {
    render(<StaticSitePanel data={makeData({ hasNestedAssets: true })} />)
    expect(screen.getByText('Asset Layout')).toBeInTheDocument()
    expect(screen.getByText('Nested')).toBeInTheDocument()
  })

  test('renders Flat layout badge when no nested assets', () => {
    render(<StaticSitePanel data={makeData({ hasNestedAssets: false })} />)
    expect(screen.getByText('Flat')).toBeInTheDocument()
  })

  test('handles zero HTML files', () => {
    render(<StaticSitePanel data={makeData({ htmlFileCount: 0 })} />)
    expect(screen.getByText('0')).toBeInTheDocument()
  })

  test('handles zero asset bytes', () => {
    render(<StaticSitePanel data={makeData({ totalAssetBytes: 0 })} />)
    expect(screen.getByText('0 B')).toBeInTheDocument()
  })
})
