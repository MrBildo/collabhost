import type { FieldEditable } from '@/api/types'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, test, vi } from 'vitest'
import { SchemaField } from './SchemaField'

const EDITABLE_ALWAYS: FieldEditable = { mode: 'always' }
const EDITABLE_LOCKED: FieldEditable = { mode: 'locked', reason: 'Set at registration' }
const EDITABLE_DERIVED: FieldEditable = { mode: 'derived', reason: 'Computed from type' }

describe('SchemaField', () => {
  describe('read mode', () => {
    test('renders text value as plain text', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Name"
          type="text"
          value="my-app"
          defaultValue=""
          editable={EDITABLE_ALWAYS}
          isEditing={false}
          onChange={vi.fn()}
        />,
      )
      expect(screen.getByText('my-app')).toBeInTheDocument()
    })

    test('renders -- for null value', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Port"
          type="number"
          value={null}
          defaultValue={null}
          editable={EDITABLE_ALWAYS}
          isEditing={false}
          onChange={vi.fn()}
        />,
      )
      expect(screen.getByText('--')).toBeInTheDocument()
    })

    test('renders Yes/No for boolean value', () => {
      const { rerender } = render(
        <SchemaField
          fieldKey="test"
          label="Auto Start"
          type="boolean"
          value={true}
          defaultValue={false}
          editable={EDITABLE_ALWAYS}
          isEditing={false}
          onChange={vi.fn()}
        />,
      )
      expect(screen.getByText('Yes')).toBeInTheDocument()

      rerender(
        <SchemaField
          fieldKey="test"
          label="Auto Start"
          type="boolean"
          value={false}
          defaultValue={false}
          editable={EDITABLE_ALWAYS}
          isEditing={false}
          onChange={vi.fn()}
        />,
      )
      expect(screen.getByText('No')).toBeInTheDocument()
    })

    test('renders select option label instead of raw value', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Restart Policy"
          type="select"
          value="onCrash"
          defaultValue="never"
          editable={EDITABLE_ALWAYS}
          options={[
            { value: 'never', label: 'Never' },
            { value: 'onCrash', label: 'On Crash' },
            { value: 'always', label: 'Always' },
          ]}
          isEditing={false}
          onChange={vi.fn()}
        />,
      )
      expect(screen.getByText('On Crash')).toBeInTheDocument()
    })

    test('renders lock badge for locked fields', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Name"
          type="text"
          value="my-app"
          defaultValue="my-app"
          editable={EDITABLE_LOCKED}
          isEditing={true}
          onChange={vi.fn()}
        />,
      )
      // Locked fields stay in read mode even when isEditing is true
      expect(screen.getByText('my-app')).toBeInTheDocument()
      expect(screen.getByText('Set at registration')).toBeInTheDocument()
    })

    test('renders derived badge for derived fields', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Port"
          type="number"
          value={8080}
          defaultValue={null}
          editable={EDITABLE_DERIVED}
          isEditing={true}
          onChange={vi.fn()}
        />,
      )
      expect(screen.getByText('8080')).toBeInTheDocument()
      expect(screen.getByText('Computed from type')).toBeInTheDocument()
    })
  })

  describe('edit mode', () => {
    test('renders text input in edit mode', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Name"
          type="text"
          value="my-app"
          defaultValue=""
          editable={EDITABLE_ALWAYS}
          isEditing={true}
          onChange={vi.fn()}
        />,
      )
      const input = screen.getByRole('textbox')
      expect(input).toHaveValue('my-app')
    })

    test('renders number input in edit mode', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Port"
          type="number"
          value={3000}
          defaultValue={3000}
          editable={EDITABLE_ALWAYS}
          isEditing={true}
          onChange={vi.fn()}
        />,
      )
      const input = screen.getByRole('spinbutton')
      expect(input).toHaveValue(3000)
    })

    test('renders boolean toggle in edit mode', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Auto Start"
          type="boolean"
          value={true}
          defaultValue={false}
          editable={EDITABLE_ALWAYS}
          isEditing={true}
          onChange={vi.fn()}
        />,
      )
      const toggle = screen.getByRole('switch')
      expect(toggle).toHaveAttribute('aria-checked', 'true')
    })

    test('renders select dropdown in edit mode', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Policy"
          type="select"
          value="never"
          defaultValue="never"
          editable={EDITABLE_ALWAYS}
          options={[
            { value: 'never', label: 'Never' },
            { value: 'always', label: 'Always' },
          ]}
          isEditing={true}
          onChange={vi.fn()}
        />,
      )
      const select = screen.getByRole('combobox')
      expect(select).toHaveValue('never')
    })

    test('calls onChange when text input changes', async () => {
      const user = userEvent.setup()
      const onChange = vi.fn()

      render(
        <SchemaField
          fieldKey="test"
          label="Name"
          type="text"
          value=""
          defaultValue=""
          editable={EDITABLE_ALWAYS}
          isEditing={true}
          onChange={onChange}
        />,
      )

      const input = screen.getByRole('textbox')
      await user.type(input, 'a')
      expect(onChange).toHaveBeenCalledWith('a')
    })

    test('calls onChange when boolean toggle is clicked', async () => {
      const user = userEvent.setup()
      const onChange = vi.fn()

      render(
        <SchemaField
          fieldKey="test"
          label="Auto Start"
          type="boolean"
          value={false}
          defaultValue={false}
          editable={EDITABLE_ALWAYS}
          isEditing={true}
          onChange={onChange}
        />,
      )

      const toggle = screen.getByRole('switch')
      await user.click(toggle)
      expect(onChange).toHaveBeenCalledWith(true)
    })
  })

  describe('restart badge', () => {
    test('shows restart badge in edit mode for requiresRestart fields', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Command"
          type="text"
          value="dotnet"
          defaultValue="dotnet"
          editable={EDITABLE_ALWAYS}
          requiresRestart={true}
          isEditing={true}
          onChange={vi.fn()}
        />,
      )
      expect(screen.getByText('Restart required')).toBeInTheDocument()
    })

    test('does not show restart badge in read mode', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Command"
          type="text"
          value="dotnet"
          defaultValue="dotnet"
          editable={EDITABLE_ALWAYS}
          requiresRestart={true}
          isEditing={false}
          onChange={vi.fn()}
        />,
      )
      expect(screen.queryByText('Restart required')).not.toBeInTheDocument()
    })

    test('does not show restart badge for locked fields even in edit mode', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Name"
          type="text"
          value="my-app"
          defaultValue="my-app"
          editable={EDITABLE_LOCKED}
          requiresRestart={true}
          isEditing={true}
          onChange={vi.fn()}
        />,
      )
      expect(screen.queryByText('Restart required')).not.toBeInTheDocument()
    })

    test('does not show restart badge when requiresRestart is false', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Policy"
          type="select"
          value="never"
          defaultValue="never"
          editable={EDITABLE_ALWAYS}
          requiresRestart={false}
          options={[
            { value: 'never', label: 'Never' },
            { value: 'always', label: 'Always' },
          ]}
          isEditing={true}
          onChange={vi.fn()}
        />,
      )
      expect(screen.queryByText('Restart required')).not.toBeInTheDocument()
    })
  })

  describe('override badge', () => {
    test('shows override badge when value differs from default', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Port"
          type="number"
          value={8080}
          defaultValue={3000}
          editable={EDITABLE_ALWAYS}
          isEditing={false}
          onChange={vi.fn()}
        />,
      )
      expect(screen.getByText('Overridden')).toBeInTheDocument()
    })

    test('does not show override badge when value equals default', () => {
      render(
        <SchemaField
          fieldKey="test"
          label="Port"
          type="number"
          value={3000}
          defaultValue={3000}
          editable={EDITABLE_ALWAYS}
          isEditing={false}
          onChange={vi.fn()}
        />,
      )
      expect(screen.queryByText('Overridden')).not.toBeInTheDocument()
    })
  })
})
