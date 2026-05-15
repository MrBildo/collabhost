import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, test, vi } from 'vitest'
import { KeyValueField } from './KeyValueField'

// KeyValueField is controlled: editing an existing key/value round-trips through
// the parent's value prop. Tests that exercise key/value EDITS need a stateful
// host so the new value flows back in (an inert vi.fn() onChange would freeze the
// rendered value). Add-row state is internal, so add-row tests don't need this.
type HostProps = {
  initial: Record<string, string>
  keyPattern?: RegExp
  keyPatternMessage?: string
  onChangeSpy?: (v: Record<string, string>) => void
}

function ControlledHost({ initial, keyPattern, keyPatternMessage, onChangeSpy }: HostProps) {
  const [value, setValue] = useState(initial)
  return (
    <KeyValueField
      value={value}
      onChange={(v) => {
        setValue(v)
        onChangeSpy?.(v)
      }}
      keyPattern={keyPattern}
      keyPatternMessage={keyPatternMessage}
    />
  )
}

// The server-authoritative #308 header-path pattern + message (mirrors the DTO).
const HEADER_KEY_PATTERN = /^\/[^\s:]+::[!#$%&'*+.^_`|~0-9A-Za-z-]+$/
const HEADER_KEY_MESSAGE =
  'Keys must be "<path>::<HeaderName>" -- a path starting with \'/\' (no spaces or colons), \'::\', then a valid HTTP header name (e.g. "/config.json::Cache-Control").'
const ENV_KEY_MESSAGE =
  'Keys must start with a letter or underscore, and contain only letters, digits, and underscores.'

describe('KeyValueField', () => {
  describe('env-var default (no keyPattern prop — no regression)', () => {
    test('accepts a POSIX-identifier key and adds it', async () => {
      const user = userEvent.setup()
      const onChange = vi.fn()
      render(<KeyValueField value={{}} onChange={onChange} />)

      await user.type(screen.getByLabelText('New entry key'), 'MY_VAR')
      await user.type(screen.getByLabelText('New entry value'), 'hello')
      await user.click(screen.getByLabelText('Add entry'))

      expect(onChange).toHaveBeenCalledWith({ MY_VAR: 'hello' })
    })

    test('rejects a header-shaped key with the env-var message (Add disabled)', async () => {
      const user = userEvent.setup()
      const onChange = vi.fn()
      render(<KeyValueField value={{}} onChange={onChange} />)

      await user.type(screen.getByLabelText('New entry key'), '/config.json::Cache-Control')

      expect(screen.getByText(ENV_KEY_MESSAGE)).toBeInTheDocument()
      expect(screen.getByLabelText('Add entry')).toBeDisabled()
    })

    test('rejects a key starting with a digit (env-var regex enforced)', async () => {
      const user = userEvent.setup()
      render(<KeyValueField value={{}} onChange={vi.fn()} />)

      await user.type(screen.getByLabelText('New entry key'), '1BAD')

      expect(screen.getByText(ENV_KEY_MESSAGE)).toBeInTheDocument()
      expect(screen.getByLabelText('Add entry')).toBeDisabled()
    })

    test('uses the env-var regex even when a custom message is absent', async () => {
      const user = userEvent.setup()
      const onChange = vi.fn()
      render(<KeyValueField value={{}} onChange={onChange} />)

      await user.type(screen.getByLabelText('New entry key'), 'GOOD_KEY')
      await user.click(screen.getByLabelText('Add entry'))

      expect(onChange).toHaveBeenCalledWith({ GOOD_KEY: '' })
    })
  })

  describe('schema-driven keyPattern (header-path fields)', () => {
    test('accepts a "/path::Header" key when given the header pattern', async () => {
      const user = userEvent.setup()
      const onChange = vi.fn()
      render(
        <KeyValueField
          value={{}}
          onChange={onChange}
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
        />,
      )

      await user.type(screen.getByLabelText('New entry key'), '/index.html::X-Frame-Options')
      await user.type(screen.getByLabelText('New entry value'), 'DENY')
      await user.click(screen.getByLabelText('Add entry'))

      expect(onChange).toHaveBeenCalledWith({ '/index.html::X-Frame-Options': 'DENY' })
    })

    test('rejects an env-var-shaped key with the server message under the header pattern', async () => {
      const user = userEvent.setup()
      render(
        <KeyValueField
          value={{}}
          onChange={vi.fn()}
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
        />,
      )

      await user.type(screen.getByLabelText('New entry key'), 'Cache-Control')

      expect(screen.getByText(HEADER_KEY_MESSAGE)).toBeInTheDocument()
      expect(screen.queryByText(ENV_KEY_MESSAGE)).not.toBeInTheDocument()
      expect(screen.getByLabelText('Add entry')).toBeDisabled()
    })

    test('rejects a single-colon key (must be the "::" separator)', async () => {
      const user = userEvent.setup()
      render(
        <KeyValueField
          value={{}}
          onChange={vi.fn()}
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
        />,
      )

      await user.type(screen.getByLabelText('New entry key'), '/config.json:Cache-Control')

      expect(screen.getByText(HEADER_KEY_MESSAGE)).toBeInTheDocument()
      expect(screen.getByLabelText('Add entry')).toBeDisabled()
    })
  })

  describe('seeded default row (/config.json::Cache-Control => no-cache)', () => {
    const seeded = { '/config.json::Cache-Control': 'no-cache' }

    test('renders the seeded key and value', () => {
      render(
        <KeyValueField
          value={seeded}
          onChange={vi.fn()}
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
        />,
      )

      expect(screen.getByLabelText('Key for /config.json::Cache-Control')).toHaveValue('/config.json::Cache-Control')
      expect(screen.getByLabelText('Value for /config.json::Cache-Control')).toHaveValue('no-cache')
    })

    test('value is editable (no-cache -> no-store)', async () => {
      const user = userEvent.setup()
      const onChangeSpy = vi.fn()
      render(
        <ControlledHost
          initial={seeded}
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
          onChangeSpy={onChangeSpy}
        />,
      )

      const valueInput = screen.getByLabelText('Value for /config.json::Cache-Control')
      await user.clear(valueInput)
      await user.type(valueInput, 'no-store')

      expect(onChangeSpy).toHaveBeenLastCalledWith({ '/config.json::Cache-Control': 'no-store' })
      expect(screen.getByLabelText('Value for /config.json::Cache-Control')).toHaveValue('no-store')
    })

    test('the seeded row is removable', async () => {
      const user = userEvent.setup()
      const onChangeSpy = vi.fn()
      render(
        <ControlledHost
          initial={seeded}
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
          onChangeSpy={onChangeSpy}
        />,
      )

      await user.click(screen.getByLabelText('Remove /config.json::Cache-Control'))

      expect(onChangeSpy).toHaveBeenCalledWith({})
      expect(screen.queryByLabelText('Value for /config.json::Cache-Control')).not.toBeInTheDocument()
    })

    test('renders the key as plain text (no input) when disabled', () => {
      render(
        <KeyValueField
          value={seeded}
          onChange={vi.fn()}
          disabled
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
        />,
      )

      expect(screen.queryByLabelText('Key for /config.json::Cache-Control')).not.toBeInTheDocument()
      expect(screen.getByText('/config.json::Cache-Control')).toBeInTheDocument()
      expect(screen.queryByLabelText('Add entry')).not.toBeInTheDocument()
    })
  })

  describe('editable keys (full per-path authoring — Bill ruling)', () => {
    test('an existing key is rendered as an editable input', () => {
      render(
        <KeyValueField
          value={{ '/a.json::ETag': 'abc' }}
          onChange={vi.fn()}
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
        />,
      )

      const keyInput = screen.getByLabelText('Key for /a.json::ETag')
      expect(keyInput.tagName).toBe('INPUT')
      expect(keyInput).toHaveValue('/a.json::ETag')
    })

    test('retargeting an existing key preserves the value at its position', async () => {
      const user = userEvent.setup()
      const onChangeSpy = vi.fn()
      render(
        <ControlledHost
          initial={{ '/a.json::ETag': 'abc' }}
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
          onChangeSpy={onChangeSpy}
        />,
      )

      // Append a character at the end — the controlled record is rebuilt at the
      // same position, so the value rides along with the renamed key.
      const keyInput = screen.getByLabelText('Key for /a.json::ETag')
      await user.type(keyInput, 's', {
        initialSelectionStart: '/a.json::ETag'.length,
        initialSelectionEnd: '/a.json::ETag'.length,
      })

      expect(onChangeSpy).toHaveBeenLastCalledWith({ '/a.json::ETags': 'abc' })
      expect(screen.getByLabelText('Key for /a.json::ETags')).toHaveValue('/a.json::ETags')
      expect(screen.getByLabelText('Value for /a.json::ETags')).toHaveValue('abc')
    })

    test('editing a key to an invalid shape surfaces the keyPattern message', async () => {
      const user = userEvent.setup()
      render(
        <ControlledHost
          initial={{ '/a.json::ETag': 'abc' }}
          keyPattern={HEADER_KEY_PATTERN}
          keyPatternMessage={HEADER_KEY_MESSAGE}
        />,
      )

      // Replace the trailing header token with a space (invalid: no spaces in
      // a header name) so the key fails the pattern without emptying the row.
      const keyInput = screen.getByLabelText('Key for /a.json::ETag')
      await user.type(keyInput, ' BAD', {
        initialSelectionStart: '/a.json::ETag'.length,
        initialSelectionEnd: '/a.json::ETag'.length,
      })

      expect(screen.getByText(HEADER_KEY_MESSAGE)).toBeInTheDocument()
      expect(screen.getByLabelText('Key for /a.json::ETag BAD')).toHaveAttribute('aria-invalid', 'true')
    })

    test('editing an env-var key is still gated to the env-var pattern (default)', async () => {
      const user = userEvent.setup()
      render(<ControlledHost initial={{ MY_VAR: 'v' }} />)

      // Append a hyphen — invalid under the env-var POSIX-identifier default.
      const keyInput = screen.getByLabelText('Key for MY_VAR')
      await user.type(keyInput, '-BAD', {
        initialSelectionStart: 'MY_VAR'.length,
        initialSelectionEnd: 'MY_VAR'.length,
      })

      expect(screen.getByText(ENV_KEY_MESSAGE)).toBeInTheDocument()
      expect(screen.getByLabelText('Key for MY_VAR-BAD')).toHaveAttribute('aria-invalid', 'true')
    })
  })
})
