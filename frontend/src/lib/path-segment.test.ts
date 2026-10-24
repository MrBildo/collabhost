import { describe, expect, test } from 'vitest'
import { encodePathSegment } from './path-segment'

describe('encodePathSegment', () => {
  test('passes an ordinary slug through unchanged', () => {
    expect(encodePathSegment('my-app-2')).toBe('my-app-2')
  })

  test('percent-encodes slashes, query and fragment characters', () => {
    expect(encodePathSegment('../users')).toBe('..%2Fusers')
    expect(encodePathSegment('a?b#c')).toBe('a%3Fb%23c')
    expect(encodePathSegment('a\\b')).toBe('a%5Cb')
  })

  test('re-encodes percent signs so an already-encoded value cannot decode twice', () => {
    expect(encodePathSegment('%2E%2E')).toBe('%252E%252E')
  })

  test('refuses empty and dot segments the URL parser would resolve as directory steps', () => {
    expect(() => encodePathSegment('')).toThrow('not a valid path segment')
    expect(() => encodePathSegment('.')).toThrow('not a valid path segment')
    expect(() => encodePathSegment('..')).toThrow('not a valid path segment')
  })

  test('an encoded traversal attempt resolves to a single segment under its parent', () => {
    // The browser's URL parser is the authority on what a fetch path resolves to.
    const resolved = new URL(`/api/v1/apps/${encodePathSegment('../users')}`, 'https://dashboard.test')
    expect(resolved.pathname).toBe('/api/v1/apps/..%2Fusers')
  })
})
