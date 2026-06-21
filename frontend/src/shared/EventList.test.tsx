import type { DashboardEvent } from '@/api/types'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, describe, expect, test } from 'vitest'
import { EventList } from './EventList'

beforeAll(() => {
  // jsdom does not implement scrollHeight/clientHeight; the auto-scroll layout
  // effect and the isAtBottom math need stable numbers.
  Object.defineProperty(HTMLDivElement.prototype, 'scrollHeight', { configurable: true, get: () => 100 })
  Object.defineProperty(HTMLDivElement.prototype, 'clientHeight', { configurable: true, get: () => 50 })
})

let _eventCounter = 0

function makeEvent(overrides: Partial<DashboardEvent> = {}): DashboardEvent {
  _eventCounter += 1
  return {
    id: `01TESTEVENT0000000000000${_eventCounter.toString().padStart(2, '0')}`,
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

  test('renders Follow button in active state by default', () => {
    render(<EventList events={[makeEvent()]} />)
    const follow = screen.getByRole('button', { name: 'Follow' })
    expect(follow).toBeInTheDocument()
    expect(follow.className).toContain('wm-filter-chip--active')
  })

  test('Follow button toggles off when clicked', async () => {
    const user = userEvent.setup()
    render(<EventList events={[makeEvent()]} />)
    const follow = screen.getByRole('button', { name: 'Follow' })
    await user.click(follow)
    expect(follow.className).not.toContain('wm-filter-chip--active')
  })

  test('Follow button renders for empty event list', () => {
    render(<EventList events={[]} />)
    expect(screen.getByRole('button', { name: 'Follow' })).toBeInTheDocument()
  })

  test('releases follow on a non-wheel scroll away from the bottom (FE-UI-01)', () => {
    // Keyboard / scrollbar / touch fire `scroll` without a `wheel` event. The
    // earlier wheel-only guard left follow un-releasable for those inputs.
    const { container } = render(<EventList events={[makeEvent()]} />)
    const follow = screen.getByRole('button', { name: 'Follow' })
    expect(follow.className).toContain('wm-filter-chip--active')

    const scroller = container.querySelector('.wm-event-list') as HTMLDivElement
    Object.defineProperty(scroller, 'scrollTop', { configurable: true, value: 0, writable: true })
    fireEvent.scroll(scroller)

    expect(follow.className).not.toContain('wm-filter-chip--active')
  })
})
