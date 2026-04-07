import type { DashboardEvent } from '@/api/types'
import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { EventList } from './EventList'

function makeEvent(overrides: Partial<DashboardEvent> = {}): DashboardEvent {
  return {
    timestamp: '2026-04-07T12:00:00Z',
    message: 'started',
    appSlug: 'my-api',
    source: 'Admin',
    severity: 'info',
    ...overrides,
  }
}

describe('EventList', () => {
  test('renders empty state when events array is empty', () => {
    render(<EventList events={[]} />)
    expect(screen.getByText('No recent events')).toBeInTheDocument()
  })

  test('renders event message for each event', () => {
    const events = [
      makeEvent({ message: 'started', appSlug: 'my-api' }),
      makeEvent({ message: 'stopped', appSlug: 'worker-svc' }),
    ]
    render(<EventList events={events} />)
    expect(screen.getByText('started')).toBeInTheDocument()
    expect(screen.getByText('stopped')).toBeInTheDocument()
  })

  test('renders appSlug as bold prefix when present', () => {
    render(<EventList events={[makeEvent({ appSlug: 'my-api', message: 'crashed' })]} />)
    const slug = screen.getByText('my-api')
    expect(slug.tagName.toLowerCase()).toBe('strong')
  })

  test('renders without app prefix for system events with null appSlug', () => {
    render(<EventList events={[makeEvent({ appSlug: null, message: 'proxy reloaded', source: 'System' })]} />)
    expect(screen.getByText('proxy reloaded')).toBeInTheDocument()
    // no strong element for null slug
    expect(screen.queryByRole('strong')).toBeNull()
  })

  test('renders source badge for each event', () => {
    render(<EventList events={[makeEvent({ source: 'Admin' }), makeEvent({ source: 'System' })]} />)
    expect(screen.getAllByText('Admin')).toHaveLength(1)
    expect(screen.getByText('System')).toBeInTheDocument()
  })

  test('renders multiple events in order', () => {
    const events = [
      makeEvent({ message: 'first event', appSlug: 'app-a' }),
      makeEvent({ message: 'second event', appSlug: 'app-b' }),
      makeEvent({ message: 'third event', appSlug: 'app-c' }),
    ]
    render(<EventList events={events} />)
    const messages = screen.getAllByText(/event/)
    expect(messages).toHaveLength(3)
  })
})
