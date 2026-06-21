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

  test('256-color foreground (38;5;n) is consumed and maps to a color', () => {
    // 38;5;196 is a bright red in the xterm-256 cube.
    const result = parseAnsiToSegments('\x1b[38;5;196mERR\x1b[0m')
    expect(result).toEqual([{ text: 'ERR', color: '--wm-red', bold: false, dim: false }])
  })

  test('256-color background (48;5;n) is consumed without leaking a color', () => {
    // Background color is not rendered (no bg support), but its args must be
    // fully consumed so the trailing index does not bleed into a fg code.
    const result = parseAnsiToSegments('\x1b[48;5;123mtext\x1b[0m')
    expect(result).toEqual([{ text: 'text', color: null, bold: false, dim: false }])
  })

  test('256-color arg does NOT bleed into a standalone SGR color (the FE-UI-03 bug)', () => {
    // Before the fix: codes parse as [38, 5, 31]; 38 and 5 are ignored but 31
    // is in SGR_COLORS, so the text wrongly renders red. After: 38;5;31 is one
    // 256-color directive (index 31 -> a green-ish cube cell), never red.
    const result = parseAnsiToSegments('\x1b[38;5;31mteal\x1b[0m')
    expect(result).not.toEqual([{ text: 'teal', color: '--wm-red', bold: false, dim: false }])
    expect(result[0]?.color).not.toBe('--wm-red')
  })

  test('truecolor foreground (38;2;r;g;b) is consumed and maps to a color', () => {
    const result = parseAnsiToSegments('\x1b[38;2;239;68;68mred\x1b[0m')
    expect(result[0]?.text).toBe('red')
    expect(result[0]?.color).toBe('--wm-red')
  })

  test('truecolor args do NOT bleed into following standalone codes', () => {
    // 38;2;1;2;31 -> a truecolor directive consuming r=1 g=2 b=31; the trailing
    // 31 must NOT be read as the red SGR code. Bold (the 1) must also not leak.
    const result = parseAnsiToSegments('\x1b[38;2;1;2;31mX\x1b[0m')
    expect(result[0]?.color).not.toBe('--wm-red')
    expect(result[0]?.bold).toBe(false)
  })

  test('256-color directive composes with a leading standalone code', () => {
    // 1 (bold) then a 256-color fg: bold applies, color comes from the cube,
    // and the cube index must not be re-read as a standalone SGR color.
    const result = parseAnsiToSegments('\x1b[1;38;5;46mok\x1b[0m')
    expect(result[0]?.text).toBe('ok')
    expect(result[0]?.bold).toBe(true)
    expect(result[0]?.color).toBe('--wm-green')
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
