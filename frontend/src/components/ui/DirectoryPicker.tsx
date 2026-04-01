import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Folder, FolderOpen, ChevronRight, Loader2 } from 'lucide-react';

import { cn } from '@/lib/utils';
import { api } from '@/lib/api';
import { Button, buttonVariants } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { ScrollArea } from '@/components/ui/scroll-area';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import type { FilesystemBrowseResponse, FilesystemEntry } from '@/types/api';

type DirectoryPickerProps = {
  value: string;
  onChange: (path: string) => void;
  placeholder?: string;
  disabled?: boolean;
  initialPath?: string;
};

/** Extracts the parent directory from a partial path for browsing. */
function getParentPath(input: string): string | null {
  if (!input) return null;
  const separatorIndex = Math.max(input.lastIndexOf('\\'), input.lastIndexOf('/'));
  if (separatorIndex < 0) return null;
  // For root paths like "C:\", return "C:\"
  const parent = input.substring(0, separatorIndex + 1);
  return parent;
}

/** Extracts the partial name being typed after the last separator. */
function getPartialName(input: string): string {
  const separatorIndex = Math.max(input.lastIndexOf('\\'), input.lastIndexOf('/'));
  if (separatorIndex < 0) return input;
  return input.substring(separatorIndex + 1);
}

function DirectoryPicker({
  value,
  onChange,
  placeholder = 'Enter directory path...',
  disabled = false,
  initialPath,
}: DirectoryPickerProps) {
  const [inputValue, setInputValue] = useState(value);
  const [showDropdown, setShowDropdown] = useState(false);
  const [entries, setEntries] = useState<FilesystemEntry[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [dialogOpen, setDialogOpen] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  // Sync external value changes
  useEffect(() => {
    setInputValue(value);
  }, [value]);

  const fetchEntries = useCallback(async (browsePath: string | null) => {
    setIsLoading(true);
    try {
      const response = await api.get<FilesystemBrowseResponse>('/filesystem/browse', {
        params: browsePath ? { path: browsePath } : undefined,
      });
      setEntries(response.data.entries);
    } catch {
      setEntries([]);
    } finally {
      setIsLoading(false);
    }
  }, []);

  const handleInputChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const newValue = event.target.value;
      setInputValue(newValue);
      onChange(newValue);

      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
      }

      debounceRef.current = setTimeout(() => {
        const parentPath = getParentPath(newValue);
        if (parentPath) {
          fetchEntries(parentPath);
          setShowDropdown(true);
        } else if (newValue.length > 0) {
          // Might be typing a drive letter — browse roots, or seed from initialPath
          fetchEntries(initialPath ?? null);
          setShowDropdown(true);
        } else {
          setShowDropdown(false);
          setEntries([]);
        }
      }, 300);
    },
    [onChange, fetchEntries, initialPath],
  );

  const handleEntrySelect = useCallback(
    (entryPath: string) => {
      setInputValue(entryPath);
      onChange(entryPath);
      setShowDropdown(false);
    },
    [onChange],
  );

  // Filter entries by partial name
  const filteredEntries = useMemo(() => {
    const partial = getPartialName(inputValue).toLowerCase();
    if (!partial) return entries;
    return entries.filter((entry) => entry.name.toLowerCase().startsWith(partial));
  }, [entries, inputValue]);

  // Close dropdown on outside click
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setShowDropdown(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Cleanup debounce on unmount
  useEffect(() => {
    return () => {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
      }
    };
  }, []);

  return (
    <div ref={containerRef} className="relative" data-slot="directory-picker">
      <div className="flex gap-2">
        <div className="relative flex-1">
          <Input
            value={inputValue}
            onChange={handleInputChange}
            onFocus={() => {
              if (entries.length > 0) setShowDropdown(true);
            }}
            placeholder={placeholder}
            disabled={disabled}
            className="font-mono text-sm"
          />
          {isLoading && (
            <Loader2 className="absolute top-1/2 right-2 size-4 -translate-y-1/2 animate-spin text-muted-foreground" />
          )}
        </div>
        <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
          <DialogTrigger
            className={cn(buttonVariants({ variant: 'outline', size: 'default' }))}
            disabled={disabled}
          >
            <FolderOpen className="size-4" />
            <span>Browse</span>
          </DialogTrigger>
          <DialogContent className="sm:max-w-lg">
            <DialogHeader>
              <DialogTitle>Browse Directories</DialogTitle>
            </DialogHeader>
            <TreeBrowser
              initialPath={inputValue || initialPath || ''}
              onSelect={(path) => {
                setInputValue(path);
                onChange(path);
                setDialogOpen(false);
              }}
            />
          </DialogContent>
        </Dialog>
      </div>

      {showDropdown && filteredEntries.length > 0 && (
        <div className="absolute z-50 mt-1 w-full rounded-lg border border-border bg-popover p-1 shadow-md">
          <ScrollArea className="max-h-48">
            {filteredEntries.map((entry) => (
              <button
                key={entry.path}
                type="button"
                className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-muted"
                onClick={() => handleEntrySelect(entry.path)}
              >
                <Folder className="size-4 shrink-0 text-muted-foreground" />
                <span className="truncate font-mono">{entry.name}</span>
              </button>
            ))}
          </ScrollArea>
        </div>
      )}
    </div>
  );
}

// -- Tree Browser (used inside Dialog) --

type TreeBrowserProps = {
  initialPath: string;
  onSelect: (path: string) => void;
};

function TreeBrowser({ initialPath, onSelect }: TreeBrowserProps) {
  const [currentPath, setCurrentPath] = useState(initialPath || '');
  const [entries, setEntries] = useState<FilesystemEntry[]>([]);
  const [parentPath, setParentPath] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [selectedPath, setSelectedPath] = useState(initialPath || '');

  const fetchDirectory = useCallback(async (browsePath: string) => {
    setIsLoading(true);
    try {
      const response = await api.get<FilesystemBrowseResponse>('/filesystem/browse', {
        params: browsePath ? { path: browsePath } : undefined,
      });
      setCurrentPath(response.data.currentPath);
      setEntries(response.data.entries);
      setParentPath(response.data.parent);
      setSelectedPath(response.data.currentPath);
    } catch {
      setEntries([]);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchDirectory(initialPath || '');
  }, [fetchDirectory, initialPath]);

  const breadcrumbs = useMemo(() => {
    if (!currentPath) return [];
    const parts: { label: string; path: string }[] = [];
    let accumulated = '';

    // Split on both separators
    const segments = currentPath.split(/[\\/]/).filter(Boolean);
    for (const segment of segments) {
      accumulated = accumulated ? `${accumulated}\\${segment}` : segment;
      // For drive letters, add the backslash
      const path =
        accumulated.includes(':') && !accumulated.endsWith('\\') && segments.indexOf(segment) === 0
          ? `${accumulated}\\`
          : accumulated;
      parts.push({ label: segment, path });
    }
    return parts;
  }, [currentPath]);

  return (
    <div className="space-y-3">
      {/* Breadcrumbs */}
      <div className="flex flex-wrap items-center gap-1 text-sm">
        <button
          type="button"
          className="text-muted-foreground hover:text-foreground"
          onClick={() => fetchDirectory('')}
        >
          Root
        </button>
        {breadcrumbs.map((crumb, index) => (
          <span key={crumb.path} className="flex items-center gap-1">
            <ChevronRight className="size-3 text-muted-foreground" />
            <button
              type="button"
              className={cn(
                'hover:text-foreground font-mono',
                index === breadcrumbs.length - 1
                  ? 'font-medium text-foreground'
                  : 'text-muted-foreground',
              )}
              onClick={() => fetchDirectory(crumb.path)}
            >
              {crumb.label}
            </button>
          </span>
        ))}
      </div>

      {/* Directory listing */}
      <ScrollArea className="h-64 rounded-lg border border-border">
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="size-5 animate-spin text-muted-foreground" />
          </div>
        ) : entries.length === 0 ? (
          <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
            No subdirectories
          </div>
        ) : (
          <div className="p-1">
            {parentPath !== null && (
              <button
                type="button"
                className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-muted"
                onClick={() => fetchDirectory(parentPath)}
              >
                <Folder className="size-4 shrink-0 text-muted-foreground" />
                <span className="font-mono text-muted-foreground">..</span>
              </button>
            )}
            {entries.map((entry) => (
              <button
                key={entry.path}
                type="button"
                className={cn(
                  'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-muted',
                  selectedPath === entry.path && 'bg-accent/10 text-accent',
                )}
                onClick={() => {
                  setSelectedPath(entry.path);
                }}
                onDoubleClick={() => fetchDirectory(entry.path)}
              >
                <Folder className="size-4 shrink-0 text-muted-foreground" />
                <span className="truncate font-mono">{entry.name}</span>
              </button>
            ))}
          </div>
        )}
      </ScrollArea>

      <DialogFooter>
        <Button variant="default" onClick={() => onSelect(selectedPath)}>
          Select
        </Button>
      </DialogFooter>
    </div>
  );
}

export { DirectoryPicker };
export type { DirectoryPickerProps };
