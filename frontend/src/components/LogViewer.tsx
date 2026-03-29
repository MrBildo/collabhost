import { useCallback, useEffect, useRef, useState } from 'react';
import { ArrowDown, RefreshCw } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { LogEntry } from '@/types/api';

type LogViewerProps = {
  entries: LogEntry[];
  totalBuffered: number;
  isRefreshing?: boolean;
  onRefresh?: () => void;
  className?: string;
};

export function LogViewer({
  entries,
  totalBuffered,
  isRefreshing,
  onRefresh,
  className,
}: LogViewerProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [isPinnedToBottom, setIsPinnedToBottom] = useState(true);
  const previousEntryCountRef = useRef(entries.length);

  const scrollToBottom = useCallback(() => {
    const container = scrollRef.current;
    if (!container) return;
    container.scrollTop = container.scrollHeight;
  }, []);

  const handleScroll = useCallback(() => {
    const container = scrollRef.current;
    if (!container) return;

    const threshold = 40;
    const isAtBottom =
      container.scrollHeight - container.scrollTop - container.clientHeight < threshold;
    setIsPinnedToBottom(isAtBottom);
  }, []);

  useEffect(() => {
    if (isPinnedToBottom && entries.length !== previousEntryCountRef.current) {
      scrollToBottom();
    }
    previousEntryCountRef.current = entries.length;
  }, [entries.length, isPinnedToBottom, scrollToBottom]);

  // Initial scroll to bottom
  useEffect(() => {
    scrollToBottom();
  }, [scrollToBottom]);

  function handlePinToBottom() {
    setIsPinnedToBottom(true);
    scrollToBottom();
  }

  return (
    <div className={cn('flex flex-col overflow-hidden rounded-lg border', className)}>
      <div className="flex items-center justify-between border-b bg-muted/50 px-3 py-1.5">
        <span className="font-mono text-xs text-muted-foreground">
          {entries.length} / {totalBuffered} lines
        </span>
        <div className="flex items-center gap-1">
          {!isPinnedToBottom && (
            <Button
              variant="ghost"
              size="icon-xs"
              onClick={handlePinToBottom}
              title="Scroll to bottom"
            >
              <ArrowDown className="h-3 w-3" />
            </Button>
          )}
          {onRefresh && (
            <Button
              variant="ghost"
              size="icon-xs"
              onClick={onRefresh}
              disabled={isRefreshing}
              title="Refresh logs"
            >
              <RefreshCw className={cn('h-3 w-3', isRefreshing && 'animate-spin')} />
            </Button>
          )}
        </div>
      </div>

      <div
        ref={scrollRef}
        onScroll={handleScroll}
        className="flex-1 overflow-auto bg-gray-900 p-2 font-mono text-sm text-gray-100 dark:bg-[hsl(222,25%,6%)]"
      >
        {entries.length === 0 ? (
          <div className="flex h-full items-center justify-center text-gray-500">
            No log entries
          </div>
        ) : (
          entries.map((entry, index) => <LogLine key={index} entry={entry} />)
        )}
      </div>
    </div>
  );
}

type LogLineProps = {
  entry: LogEntry;
};

function LogLine({ entry }: LogLineProps) {
  const time = formatTimestamp(entry.timestamp);
  const isStderr = entry.stream === 'stderr';

  return (
    <div className="flex gap-2 leading-5 hover:bg-white/5">
      <span className="shrink-0 select-none text-gray-500">{time}</span>
      {isStderr && <span className="shrink-0 select-none text-red-400">ERR</span>}
      <span className={cn('min-w-0 break-all whitespace-pre-wrap', isStderr && 'text-red-300')}>
        {entry.content}
      </span>
    </div>
  );
}

function formatTimestamp(iso: string): string {
  try {
    const date = new Date(iso);
    return date.toLocaleTimeString('en-US', {
      hour12: false,
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  } catch {
    return '??:??:??';
  }
}
