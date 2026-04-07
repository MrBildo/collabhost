import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { RoleBadge } from './RoleBadge'

describe('RoleBadge', () => {
  test('renders Administrator label for administrator role', () => {
    // biome-ignore lint/a11y/useValidAriaRole: role is a component prop, not an ARIA role attribute
    render(<RoleBadge role="administrator" />)
    expect(screen.getByText('Administrator')).toBeInTheDocument()
  })

  test('renders Agent label for agent role', () => {
    // biome-ignore lint/a11y/useValidAriaRole: role is a component prop, not an ARIA role attribute
    render(<RoleBadge role="agent" />)
    expect(screen.getByText('Agent')).toBeInTheDocument()
  })

  test('applies administrator role class', () => {
    // biome-ignore lint/a11y/useValidAriaRole: role is a component prop, not an ARIA role attribute
    render(<RoleBadge role="administrator" />)
    const badge = screen.getByText('Administrator')
    expect(badge).toHaveClass('wm-role-badge--administrator')
  })

  test('applies agent role class', () => {
    // biome-ignore lint/a11y/useValidAriaRole: role is a component prop, not an ARIA role attribute
    render(<RoleBadge role="agent" />)
    const badge = screen.getByText('Agent')
    expect(badge).toHaveClass('wm-role-badge--agent')
  })

  test('applies base role badge class', () => {
    // biome-ignore lint/a11y/useValidAriaRole: role is a component prop, not an ARIA role attribute
    render(<RoleBadge role="agent" />)
    const badge = screen.getByText('Agent')
    expect(badge).toHaveClass('wm-role-badge')
  })

  test('applies medium size class when size is md', () => {
    // biome-ignore lint/a11y/useValidAriaRole: role is a component prop, not an ARIA role attribute
    render(<RoleBadge role="agent" size="md" />)
    const badge = screen.getByText('Agent')
    expect(badge).toHaveClass('wm-role-badge--md')
  })

  test('does not apply medium size class by default', () => {
    // biome-ignore lint/a11y/useValidAriaRole: role is a component prop, not an ARIA role attribute
    render(<RoleBadge role="agent" />)
    const badge = screen.getByText('Agent')
    expect(badge).not.toHaveClass('wm-role-badge--md')
  })

  test('has accessible aria-label', () => {
    // biome-ignore lint/a11y/useValidAriaRole: role is a component prop, not an ARIA role attribute
    render(<RoleBadge role="administrator" />)
    const badge = screen.getByLabelText('Role: Administrator')
    expect(badge).toBeInTheDocument()
  })
})
