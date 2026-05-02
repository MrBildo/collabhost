/**
 * Formats an action-mutation error into an operator-readable message.
 *
 * Action mutations (Start/Stop/Restart/Kill) fail through `ApiError`,
 * whose Error.message looks like `API 409: <response body>`. We strip the
 * prefix, recognise common status codes, and fall back to the body text.
 *
 * The `verb` argument is the action label ("Start", "Stop", etc.) so the
 * banner reads like "Start failed: ...". Pass undefined for a generic
 * "Action failed" prefix when multiple mutations share one banner.
 */
function formatActionError(error: unknown, verb?: string): string {
  const prefix = verb ? `${verb} failed` : 'Action failed'

  if (!(error instanceof Error)) {
    return prefix
  }

  // ApiError shape: `API <code>: <body>`
  const match = error.message.match(/^API (\d{3}): (.*)$/s)
  if (!match) {
    return `${prefix}: ${error.message}`
  }

  const status = Number(match[1] ?? '0')
  const body = (match[2] ?? '').trim()

  if (status === 409) {
    return `${prefix}: state conflict${body ? ` — ${body}` : ''}`
  }
  if (status === 404) {
    return `${prefix}: app not found`
  }
  if (status === 403) {
    return `${prefix}: not authorized`
  }
  if (status >= 500) {
    return `${prefix}: server error (${status})${body ? ` — ${body}` : ''}`
  }

  return `${prefix}: ${body || `HTTP ${status}`}`
}

export { formatActionError }
