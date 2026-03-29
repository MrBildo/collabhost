import { useCallback, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getAdminKey } from '@/lib/api';
import type { UpdateSseLogEvent, UpdateSseResultEvent, UpdateSseStatusEvent } from '@/types/api';

export type UpdatePhase = 'idle' | 'connecting' | 'running' | 'complete' | 'failed';

export type UpdateEvent =
  | { type: 'status'; data: UpdateSseStatusEvent }
  | { type: 'log'; data: UpdateSseLogEvent }
  | { type: 'result'; data: UpdateSseResultEvent };

export function useAppUpdate(appId: string) {
  const [phase, setPhase] = useState<UpdatePhase>('idle');
  const [events, setEvents] = useState<UpdateEvent[]>([]);
  const abortRef = useRef<AbortController | null>(null);
  const queryClient = useQueryClient();

  const start = useCallback(async () => {
    if (phase === 'connecting' || phase === 'running') return;

    setPhase('connecting');
    setEvents([]);

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      const key = getAdminKey();
      const headers: Record<string, string> = {
        Accept: 'text/event-stream',
      };
      if (key) {
        headers['X-User-Key'] = key;
      }

      const response = await fetch(`/api/v1/apps/${appId}/update`, {
        method: 'POST',
        headers,
        signal: controller.signal,
      });

      if (!response.ok) {
        const text = await response.text();
        setEvents((previous) => [
          ...previous,
          {
            type: 'log',
            data: {
              stream: 'stderr',
              line: `HTTP ${response.status}: ${text || response.statusText}`,
            },
          },
        ]);
        setPhase('failed');
        return;
      }

      setPhase('running');

      const reader = response.body?.getReader();
      if (!reader) {
        setPhase('failed');
        return;
      }

      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';

        let currentEventType = '';
        for (const line of lines) {
          if (line.startsWith('event:')) {
            currentEventType = line.slice(6).trim();
          } else if (line.startsWith('data:')) {
            const jsonString = line.slice(5).trim();
            if (!jsonString || !currentEventType) continue;

            try {
              const parsed: unknown = JSON.parse(jsonString);
              const event = buildEvent(currentEventType, parsed);
              if (event) {
                setEvents((previous) => [...previous, event]);

                if (event.type === 'result') {
                  setPhase(event.data.success ? 'complete' : 'failed');
                  queryClient.invalidateQueries({ queryKey: ['apps'] });
                  queryClient.invalidateQueries({ queryKey: ['apps', appId] });
                }
              }
            } catch {
              // Ignore malformed JSON lines
            }

            currentEventType = '';
          }
        }
      }

      // If stream ended without a result event, mark as complete
      setPhase((current) => (current === 'running' ? 'complete' : current));
      queryClient.invalidateQueries({ queryKey: ['apps'] });
      queryClient.invalidateQueries({ queryKey: ['apps', appId] });
    } catch (error: unknown) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return;
      }
      setPhase('failed');
      const message = error instanceof Error ? error.message : String(error);
      setEvents((previous) => [
        ...previous,
        { type: 'log', data: { stream: 'stderr', line: `Connection error: ${message}` } },
      ]);
    }
  }, [appId, phase, queryClient]);

  const reset = useCallback(() => {
    if (abortRef.current) {
      abortRef.current.abort();
      abortRef.current = null;
    }
    setPhase('idle');
    setEvents([]);
  }, []);

  return { phase, events, start, reset };
}

function buildEvent(eventType: string, data: unknown): UpdateEvent | null {
  switch (eventType) {
    case 'status':
      return { type: 'status', data: data as UpdateSseStatusEvent };
    case 'log':
      return { type: 'log', data: data as UpdateSseLogEvent };
    case 'result':
      return { type: 'result', data: data as UpdateSseResultEvent };
    default:
      return null;
  }
}
