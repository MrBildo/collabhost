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

// War Machine palette anchors in sRGB, paired with the design-system token the
// nearest-match search resolves to. 256-color and truecolor SGR directives
// carry arbitrary RGB; the viewer has a fixed token palette (no inline hex), so
// we snap an incoming RGB to the closest anchor. Greys map to the dim/bright
// text tokens so monochrome 256-cube ramps don't all collapse to one accent.
const PALETTE_ANCHORS: { token: string; r: number; g: number; b: number }[] = [
  { token: '--wm-red', r: 0xef, g: 0x44, b: 0x44 },
  { token: '--wm-green', r: 0x22, g: 0xc5, b: 0x5e },
  { token: '--wm-yellow', r: 0xea, g: 0xb3, b: 0x08 },
  { token: '--wm-amber', r: 0xf5, g: 0x9e, b: 0x0b },
  { token: '--wm-blue', r: 0x3b, g: 0x82, b: 0xf6 },
  { token: '--wm-magenta', r: 0xd9, g: 0x46, b: 0xef },
  { token: '--wm-cyan', r: 0x22, g: 0xd3, b: 0xee },
  { token: '--wm-text-bright', r: 0xe5, g: 0xe5, b: 0xe5 },
  { token: '--wm-text-dim', r: 0x77, g: 0x77, b: 0x77 },
]

/** Snap an arbitrary RGB triple to the nearest War Machine palette token. */
function nearestToken(r: number, g: number, b: number): string {
  // Seed with a concrete token (not PALETTE_ANCHORS[0], which is `T | undefined`
  // under noUncheckedIndexedAccess). Any seed is overwritten on the first
  // iteration since bestDistance starts at +Infinity; PALETTE_ANCHORS is never empty.
  let bestToken = '--wm-text-bright'
  let bestDistance = Number.POSITIVE_INFINITY
  for (const anchor of PALETTE_ANCHORS) {
    const dr = r - anchor.r
    const dg = g - anchor.g
    const db = b - anchor.b
    const distance = dr * dr + dg * dg + db * db
    if (distance < bestDistance) {
      bestDistance = distance
      bestToken = anchor.token
    }
  }
  return bestToken
}

/** Resolve an xterm 256-color index (0-255) to an sRGB triple. */
function xterm256ToRgb(index: number): { r: number; g: number; b: number } {
  // 0-15: the standard + bright 16-color set.
  const BASE_16: [number, number, number][] = [
    [0, 0, 0],
    [205, 0, 0],
    [0, 205, 0],
    [205, 205, 0],
    [0, 0, 238],
    [205, 0, 205],
    [0, 205, 205],
    [229, 229, 229],
    [127, 127, 127],
    [255, 0, 0],
    [0, 255, 0],
    [255, 255, 0],
    [92, 92, 255],
    [255, 0, 255],
    [0, 255, 255],
    [255, 255, 255],
  ]
  if (index < 16) {
    const [r, g, b] = BASE_16[index] ?? [0, 0, 0]
    return { r, g, b }
  }
  // 16-231: a 6x6x6 color cube. Each channel steps through {0,95,135,175,215,255}.
  if (index < 232) {
    const cube = index - 16
    const steps = [0, 95, 135, 175, 215, 255]
    // The `% 6` keeps every index in 0-5, so these lookups are always defined;
    // `?? 0` satisfies noUncheckedIndexedAccess without a non-null assertion.
    return {
      r: steps[Math.floor(cube / 36) % 6] ?? 0,
      g: steps[Math.floor(cube / 6) % 6] ?? 0,
      b: steps[cube % 6] ?? 0,
    }
  }
  // 232-255: a 24-step greyscale ramp from 8 to 238.
  const grey = 8 + (index - 232) * 10
  return { r: grey, g: grey, b: grey }
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

      // Index-based walk so the 38/48 extended-color directives can consume
      // their trailing args (38;5;n / 38;2;r;g;b) rather than letting those
      // args bleed back into the loop as standalone SGR codes (FE-UI-03).
      // Out-of-range reads fall back to NaN, which Number.isFinite rejects.
      let i = 0
      while (i < codes.length) {
        const code = codes[i] ?? Number.NaN
        if (code === 38 || code === 48) {
          // Extended color: 38=foreground, 48=background. The mode arg decides
          // how many args follow. Background is consumed but not rendered.
          const mode = codes[i + 1]
          if (mode === 5) {
            // 38;5;n — 256-color palette index.
            const paletteIndex = codes[i + 2] ?? Number.NaN
            if (code === 38 && Number.isFinite(paletteIndex)) {
              const { r, g, b } = xterm256ToRgb(paletteIndex)
              color = nearestToken(r, g, b)
            }
            i += 3
          } else if (mode === 2) {
            // 38;2;r;g;b — 24-bit truecolor.
            const r = codes[i + 2] ?? Number.NaN
            const g = codes[i + 3] ?? Number.NaN
            const b = codes[i + 4] ?? Number.NaN
            if (code === 38 && Number.isFinite(r) && Number.isFinite(g) && Number.isFinite(b)) {
              color = nearestToken(r, g, b)
            }
            i += 5
          } else {
            // Malformed/unknown extended-color mode: skip the introducer only.
            i += 1
          }
          continue
        }
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
        // All other codes (other background forms, attributes, etc.) are ignored
        i += 1
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
