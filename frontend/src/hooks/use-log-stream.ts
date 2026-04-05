import type { AppStatus, StreamEntry } from '@/api/types'
import { API_BASE, AUTH_STORAGE_KEY, LOG_BUFFER_CAP } from '@/lib/constants'
import { useCallback, useEffect, useRef, useState } from 'react'

const RECONNECT_DELAY_MS = 3_000

type UseLogStreamResult = {
  entries: StreamEntry[]
  isStreaming: boolean
  isConnected: boolean
  error: string | null
}

function useLogStream(slug: string, options?: { enabled?: boolean }): UseLogStreamResult {
  const isEnabled = options?.enabled ?? true
  const [renderEntries, setRenderEntries] = useState<StreamEntry[]>([])
  const [isConnected, setIsConnected] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [connectAttempt, setConnectAttempt] = useState(0)

  const entriesRef = useRef<StreamEntry[]>([])
  const maxIdRef = useRef<number>(0)
  const rafScheduledRef = useRef(false)
  const rafIdRef = useRef<number>(0)
  const reconnectTimerRef = useRef<ReturnType<typeof setTimeout>>(undefined)

  const scheduleReconnect = useCallback(() => {
    if (reconnectTimerRef.current !== undefined) return
    reconnectTimerRef.current = setTimeout(() => {
      reconnectTimerRef.current = undefined
      setConnectAttempt((n) => n + 1)
    }, RECONNECT_DELAY_MS)
  }, [])

  // biome-ignore lint/correctness/useExhaustiveDependencies: connectAttempt is an intentional re-trigger signal for reconnection after the EventSource is permanently closed
  useEffect(() => {
    if (!slug || !isEnabled) return

    const authKey = localStorage.getItem(AUTH_STORAGE_KEY)
    if (!authKey) {
      setError('No auth key')
      return
    }

    const url = `${API_BASE}/apps/${slug}/logs/stream?key=${authKey}`
    const es = new EventSource(url)

    function scheduleFlush(): void {
      if (rafScheduledRef.current) return
      rafScheduledRef.current = true
      rafIdRef.current = requestAnimationFrame(() => {
        if (entriesRef.current.length > LOG_BUFFER_CAP) {
          entriesRef.current = entriesRef.current.slice(-LOG_BUFFER_CAP)
        }
        setRenderEntries([...entriesRef.current])
        rafScheduledRef.current = false
      })
    }

    es.addEventListener('log', (e: MessageEvent) => {
      const data = JSON.parse(e.data) as {
        id: number
        timestamp: string
        stream: 'stdout' | 'stderr'
        content: string
        level: string | null
      }

      // Dedup: skip events we already have
      if (data.id <= maxIdRef.current) return

      // Gap detection: if incoming id jumps past what we expect
      if (maxIdRef.current > 0 && data.id > maxIdRef.current + 1) {
        const gap = data.id - maxIdRef.current - 1
        // If the gap exceeds the buffer cap, reset entirely
        if (gap > LOG_BUFFER_CAP) {
          entriesRef.current = []
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
      const data = JSON.parse(e.data) as { state: string; timestamp: string }
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
      // CONNECTING state: browser is auto-reconnecting — don't update
      // isConnected to avoid UI flicker between SSE and polling modes
    }

    return () => {
      es.close()
      cancelAnimationFrame(rafIdRef.current)
      rafScheduledRef.current = false
    }
  }, [slug, isEnabled, connectAttempt, scheduleReconnect])

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

export { useLogStream }
export type { UseLogStreamResult }
