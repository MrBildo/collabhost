type AnsiSegment = {
  text: string
  color: string | null
  bold: boolean
  dim: boolean
}

const SGR_COLORS: Record<number, string | null> = {
  30: '--wm-text-dim',
  31: '--wm-red',
  32: '--wm-green',
  33: '--wm-yellow',
  34: '--wm-blue',
  35: '--wm-magenta',
  36: '--wm-cyan',
  37: '--wm-text-bright',
  39: null,
  90: '--wm-text-dim',
  91: '--wm-red',
  92: '--wm-green',
  93: '--wm-amber',
  94: '--wm-blue',
  95: '--wm-magenta',
  96: '--wm-cyan',
  97: '--wm-text-bright',
}

// biome-ignore lint/suspicious/noControlCharactersInRegex: ANSI escape codes use control characters by definition
const ANSI_SEQUENCE_RE = /\x1b(?:\[[0-9;]*[A-Za-z]|\].*?(?:\x07|\x1b\\))/g

function parseAnsiToSegments(text: string): AnsiSegment[] {
  if (text === '') return []

  const segments: AnsiSegment[] = []
  let color: string | null = null
  let bold = false
  let dim = false
  let lastIndex = 0

  for (const match of text.matchAll(ANSI_SEQUENCE_RE)) {
    const matchStart = match.index
    if (matchStart > lastIndex) {
      segments.push({ text: text.slice(lastIndex, matchStart), color, bold, dim })
    }
    lastIndex = matchStart + match[0].length

    const seq = match[0]
    // Only process CSI SGR sequences (ending with 'm')
    if (seq[1] === '[' && seq[seq.length - 1] === 'm') {
      const params = seq.slice(2, -1)
      const codes = params === '' ? [0] : params.split(';').map(Number)

      for (const code of codes) {
        if (code === 0) {
          color = null
          bold = false
          dim = false
        } else if (code === 1) {
          bold = true
        } else if (code === 2) {
          dim = true
        } else if (code === 22) {
          bold = false
          dim = false
        } else if (code in SGR_COLORS) {
          color = SGR_COLORS[code] ?? null
        }
        // All other codes (background, 256-color, truecolor, etc.) are silently ignored
      }
    }
    // OSC and other non-SGR sequences are silently discarded
  }

  // Remaining text after the last escape sequence
  if (lastIndex < text.length) {
    segments.push({ text: text.slice(lastIndex), color, bold, dim })
  }

  return segments
}

export { parseAnsiToSegments }
export type { AnsiSegment }
