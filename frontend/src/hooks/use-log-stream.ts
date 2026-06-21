import type { AppStatus, StreamEntry } from '@/api/types'
import { API_BASE, AUTH_STORAGE_KEY, LOG_BUFFER_CAP } from '@/lib/constants'
import { useCallback, useEffect, useRef, useState } from 'react'

const RECONNECT_DELAY_MS = 3_000
const LIVENESS_CHECK_INTERVAL_MS = 10_000
const LIVENESS_TIMEOUT_MS = 45_000

/**
 * Trim a stream-entry buffer to the LOG_BUFFER_CAP tail, returning a new array
 * when the trim is needed and the same reference when it is not.
 *
 * Shared by both flush paths — the RAF-batched flush (scheduleFlush) and the
 * cleanup flush (effect teardown) — to keep the buffer-cap contract in one
 * place. The third buffer-cap policy site in this file (the gap-overflow
 * branch in the 'log' handler) is intentionally NOT routed through this
 * helper: gap-overflow is a full RESET (to a lone 'reset' disclosure marker,
 * not silently to `[]` — FE-SSE-01) because the missing range cannot be
 * recovered, which is a different policy from "trim to fit." (#319, #425)
 */
function trimToBufferCap(entries: StreamEntry[]): StreamEntry[] {
  return entries.length > LOG_BUFFER_CAP ? entries.slice(-LOG_BUFFER_CAP) : entries
}

type UseLogStreamOptions = {
  enabled?: boolean
  /** When this value changes, the EventSource is closed and reopened.
   *  Useful for forcing a reconnect when the app status changes via polling
   *  (e.g., the polled status transitions from 'stopping' to 'stopped' to 'running'
   *  while the SSE connection is silently dead). */
  resetKey?: string | number | undefined
}

type UseLogStreamResult = {
  entries: StreamEntry[]
  isStreaming: boolean
  isConnected: boolean
  error: string | null
}

function useLogStream(slug: string, options?: UseLogStreamOptions): UseLogStreamResult {
  const isEnabled = options?.enabled ?? true
  const resetKey = options?.resetKey
  const [renderEntries, setRenderEntries] = useState<StreamEntry[]>([])
  const [isConnected, setIsConnected] = useState(false)
  const prevSlugRef = useRef(slug)
  const [error, setError] = useState<string | null>(null)
  const [connectAttempt, setConnectAttempt] = useState(0)

  const entriesRef = useRef<StreamEntry[]>([])
  const maxIdRef = useRef<number>(0)
  const rafScheduledRef = useRef(false)
  const rafIdRef = useRef<number>(0)
  const reconnectTimerRef = useRef<ReturnType<typeof setTimeout>>(undefined)
  const lastEventTimeRef = useRef<number>(0)

  const scheduleReconnect = useCallback(() => {
    if (reconnectTimerRef.current !== undefined) return
    reconnectTimerRef.current = setTimeout(() => {
      reconnectTimerRef.current = undefined
      setConnectAttempt((n) => n + 1)
    }, RECONNECT_DELAY_MS)
  }, [])

  // biome-ignore lint/correctness/useExhaustiveDependencies: connectAttempt is an intentional re-trigger signal; resetKey forces reconnect on external state change
  useEffect(() => {
    if (!slug || !isEnabled) return

    // On slug change (navigating to a different app), fully reset so the
    // new app's history burst isn't deduped against stale IDs.
    // On same-app reconnect (resetKey/connectAttempt change), preserve
    // BOTH entriesRef AND maxIdRef — the history burst naturally deduplicates
    // because incoming entries with id <= maxIdRef are skipped. Only genuinely
    // new entries (id > maxIdRef) get appended. This prevents the duplicate
    // entry inflation that occurs when maxIdRef is reset to 0.
    if (prevSlugRef.current !== slug) {
      entriesRef.current = []
      maxIdRef.current = 0
      setRenderEntries([])
      prevSlugRef.current = slug
    }

    const authKey = localStorage.getItem(AUTH_STORAGE_KEY)
    if (!authKey) {
      setError('No auth key')
      return
    }

    const lastId = maxIdRef.current > 0 ? `&lastEventId=${maxIdRef.current}` : ''
    const url = `${API_BASE}/apps/${slug}/logs/stream?key=${authKey}${lastId}`
    const es = new EventSource(url)

    function scheduleFlush(): void {
      if (rafScheduledRef.current) return
      rafScheduledRef.current = true
      rafIdRef.current = requestAnimationFrame(() => {
        entriesRef.current = trimToBufferCap(entriesRef.current)
        setRenderEntries([...entriesRef.current])
        rafScheduledRef.current = false
      })
    }

    function markActivity(): void {
      lastEventTimeRef.current = Date.now()
    }

    es.addEventListener('log', (e: MessageEvent) => {
      // Guard the parse: a malformed frame must not throw uncaught inside the
      // EventSource listener (which would kill processing of that frame and is
      // invisible to the operator). Drop the bad frame and keep streaming (FE-SSE-02).
      let data: {
        id: number
        timestamp: string
        stream: 'stdout' | 'stderr'
        content: string
        level: string | null
      }
      try {
        data = JSON.parse(e.data)
      } catch {
        return
      }

      markActivity()

      // Dedup: skip events we already have
      if (data.id <= maxIdRef.current) return

      // Gap detection: if incoming id jumps past what we expect
      if (maxIdRef.current > 0 && data.id > maxIdRef.current + 1) {
        const gap = data.id - maxIdRef.current - 1
        // If the gap exceeds the buffer cap, the dropped range is unrecoverable
        // and larger than anything we hold, so reset. Disclose the loss with a
        // 'reset' marker (FE-SSE-01) — a bigger drop must be LOUDER than the
        // bounded 'gap' marker below, not silent (the inversion the audit named).
        if (gap > LOG_BUFFER_CAP) {
          entriesRef.current = [{ type: 'reset', dropped: gap }]
        } else {
          entriesRef.current.push({ type: 'gap' })
        }
      }

      maxIdRef.current = data.id
      entriesRef.current.push({
        type: 'log',
        entry: {
          id: data.id,
          timestamp: data.timestamp,
          stream: data.stream,
          content: data.content,
          level: data.level ?? undefined,
        },
      })
      scheduleFlush()
    })

    es.addEventListener('status', (e: MessageEvent) => {
      // Same parse guard as the 'log' listener (FE-SSE-02).
      let data: { state: string; timestamp: string }
      try {
        data = JSON.parse(e.data)
      } catch {
        return
      }
      markActivity()
      entriesRef.current.push({
        type: 'status',
        state: data.state as AppStatus,
        timestamp: data.timestamp,
      })
      scheduleFlush()
    })

    es.addEventListener('closed', () => {
      es.close()
      setIsConnected(false)
      scheduleReconnect()
    })

    es.onopen = () => {
      markActivity()
      setIsConnected(true)
      setError(null)
    }

    es.onerror = () => {
      // EventSource with readyState CLOSED will not auto-reconnect
      if (es.readyState === EventSource.CLOSED) {
        setIsConnected(false)
        setError('Connection lost')
        scheduleReconnect()
      }
      // CONNECTING state: browser is auto-reconnecting -- don't update
      // isConnected to avoid UI flicker between SSE and polling modes
    }

    // Liveness check: detect silently dead connections.
    // When the backend crashes (e.g., PeriodicTimer bug during app stop),
    // the proxy may keep the browser-side connection alive indefinitely.
    // EventSource never fires onerror, so the hook has no signal that the
    // connection is dead. This interval detects the gap and forces a reconnect.
    // The resetKey mechanism handles most cases quickly (within one poll cycle),
    // but this is a safety net for edge cases where polling doesn't catch it.
    const livenessInterval = setInterval(() => {
      if (
        es.readyState === EventSource.OPEN &&
        lastEventTimeRef.current > 0 &&
        Date.now() - lastEventTimeRef.current > LIVENESS_TIMEOUT_MS
      ) {
        es.close()
        setIsConnected(false)
        scheduleReconnect()
      }
    }, LIVENESS_CHECK_INTERVAL_MS)

    return () => {
      es.close()
      clearInterval(livenessInterval)
      // If a RAF flush is pending, perform it synchronously before cancelling.
      // Without this, a resetKey change (e.g., undefined -> 'running' when
      // detailQuery resolves on first visit) cancels the RAF that would flush
      // the initial history burst. The next connection's burst is then deduped
      // against maxIdRef, resulting in zero entries rendered.
      if (rafScheduledRef.current) {
        cancelAnimationFrame(rafIdRef.current)
        rafScheduledRef.current = false
        entriesRef.current = trimToBufferCap(entriesRef.current)
        setRenderEntries([...entriesRef.current])
      } else {
        cancelAnimationFrame(rafIdRef.current)
      }
    }
  }, [slug, isEnabled, resetKey, connectAttempt, scheduleReconnect])

  // Clean up reconnect timer on unmount or when disabled
  useEffect(() => {
    if (!isEnabled) {
      clearTimeout(reconnectTimerRef.current)
      reconnectTimerRef.current = undefined
    }
    return () => {
      clearTimeout(reconnectTimerRef.current)
      reconnectTimerRef.current = undefined
    }
  }, [isEnabled])

  const isStreaming = isConnected && !error

  return { entries: renderEntries, isStreaming, isConnected, error }
}

export { trimToBufferCap, useLogStream }
export type { UseLogStreamResult }
