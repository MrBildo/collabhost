import { describe, expect, test } from 'vitest'
import { camelToTitle } from './probe-format'

describe('camelToTitle', () => {
  test('converts simple camelCase to title case', () => {
    expect(camelToTitle('moduleSystem')).toBe('Module System')
  })

  test('converts single word', () => {
    expect(camelToTitle('version')).toBe('Version')
  })

  test('handles consecutive capitals', () => {
    expect(camelToTitle('isAspNetCore')).toBe('Is Asp Net Core')
  })

  test('handles already capitalized first letter', () => {
    expect(camelToTitle('ServerGc')).toBe('Server Gc')
  })

  test('handles single character', () => {
    expect(camelToTitle('a')).toBe('A')
  })
})
