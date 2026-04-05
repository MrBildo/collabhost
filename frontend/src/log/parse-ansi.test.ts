import { describe, expect, test } from 'vitest'
import { parseAnsiToSegments } from './parse-ansi'

describe('parseAnsiToSegments', () => {
  test('plain text returns single segment', () => {
    const result = parseAnsiToSegments('hello world')
    expect(result).toEqual([{ text: 'hello world', color: null, bold: false, dim: false }])
  })

  test('red text returns colored segment', () => {
    const result = parseAnsiToSegments('\x1b[31mERROR\x1b[0m')
    expect(result).toEqual([{ text: 'ERROR', color: '--wm-red', bold: false, dim: false }])
  })

  test('bold text', () => {
    const result = parseAnsiToSegments('\x1b[1mIMPORTANT\x1b[0m')
    expect(result).toEqual([{ text: 'IMPORTANT', color: null, bold: true, dim: false }])
  })

  test('combined bold and color', () => {
    const result = parseAnsiToSegments('\x1b[1;31mFATAL\x1b[0m')
    expect(result).toEqual([{ text: 'FATAL', color: '--wm-red', bold: true, dim: false }])
  })

  test('multiple colored segments', () => {
    const result = parseAnsiToSegments('\x1b[32minfo\x1b[0m: started')
    expect(result).toEqual([
      { text: 'info', color: '--wm-green', bold: false, dim: false },
      { text: ': started', color: null, bold: false, dim: false },
    ])
  })

  test('reset clears all state', () => {
    const result = parseAnsiToSegments('\x1b[1;31mA\x1b[0mB')
    expect(result).toEqual([
      { text: 'A', color: '--wm-red', bold: true, dim: false },
      { text: 'B', color: null, bold: false, dim: false },
    ])
  })

  test('unknown SGR codes are ignored', () => {
    const result = parseAnsiToSegments('\x1b[48;5;123mtext\x1b[0m')
    expect(result).toEqual([{ text: 'text', color: null, bold: false, dim: false }])
  })

  test('OSC sequences are stripped', () => {
    const result = parseAnsiToSegments('\x1b]0;title\x07visible')
    expect(result).toEqual([{ text: 'visible', color: null, bold: false, dim: false }])
  })

  test('empty input returns empty array', () => {
    const result = parseAnsiToSegments('')
    expect(result).toEqual([])
  })

  test('bright colors map correctly', () => {
    const result = parseAnsiToSegments('\x1b[93mwarn\x1b[0m')
    expect(result).toEqual([{ text: 'warn', color: '--wm-amber', bold: false, dim: false }])
  })

  test('dim text', () => {
    const result = parseAnsiToSegments('\x1b[2mdimmed\x1b[0m')
    expect(result).toEqual([{ text: 'dimmed', color: null, bold: false, dim: true }])
  })

  test('nested sequences without reset', () => {
    const result = parseAnsiToSegments('\x1b[31mred\x1b[32mgreen')
    expect(result).toEqual([
      { text: 'red', color: '--wm-red', bold: false, dim: false },
      { text: 'green', color: '--wm-green', bold: false, dim: false },
    ])
  })

  test('no empty segments between consecutive escapes', () => {
    const result = parseAnsiToSegments('\x1b[31m\x1b[1mbold-red')
    expect(result).toEqual([{ text: 'bold-red', color: '--wm-red', bold: true, dim: false }])
  })
})
