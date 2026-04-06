import { ActionButton } from '@/actions/ActionButton'
import { useBrowseDirectories } from '@/hooks/use-browse-directories'
import { EmptyState } from '@/shared/EmptyState'
import { Spinner } from '@/shared/Spinner'
import { useEffect, useRef, useState } from 'react'

type DirectoryBrowserProps = {
  isOpen: boolean
  initialPath: string
  onSelect: (path: string) => void
  onCancel: () => void
}

function buildBreadcrumbs(currentPath: string): Array<{ label: string; path: string }> {
  if (!currentPath) return []

  // Handle Windows paths like C:\Projects\collab
  // and Unix paths like /home/user
  const parts: Array<{ label: string; path: string }> = []

  // Windows drive root detection (e.g., "C:\")
  const driveMatch = currentPath.match(/^([A-Za-z]):\\/)
  const drivePart = driveMatch?.[1]
  if (drivePart) {
    const driveLetter = drivePart.toUpperCase()
    parts.push({ label: `${driveLetter}:`, path: `${driveLetter}:\\` })
    const remainder = currentPath.slice(3)
    if (remainder) {
      const segments = remainder.split('\\').filter(Boolean)
      let accumulated = `${driveLetter}:\\`
      for (const segment of segments) {
        accumulated = `${accumulated}${segment}\\`
        parts.push({ label: segment, path: accumulated })
      }
    }
    return parts
  }

  // Unix paths
  if (currentPath.startsWith('/')) {
    parts.push({ label: '/', path: '/' })
    const segments = currentPath.slice(1).split('/').filter(Boolean)
    let accumulated = '/'
    for (const segment of segments) {
      accumulated = `${accumulated}${segment}/`
      parts.push({ label: segment, path: accumulated })
    }
    return parts
  }

  // Fallback: just show the full path
  parts.push({ label: currentPath, path: currentPath })
  return parts
}

function DirectoryBrowser({ isOpen, initialPath, onSelect, onCancel }: DirectoryBrowserProps) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const [browsePath, setBrowsePath] = useState(initialPath)

  const { data, isLoading, error } = useBrowseDirectories(browsePath, isOpen)

  // Reset browse path when dialog opens with a new initial path
  useEffect(() => {
    if (isOpen) {
      setBrowsePath(initialPath)
    }
  }, [isOpen, initialPath])

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return

    if (isOpen && !dialog.open) {
      dialog.showModal()
    } else if (!isOpen && dialog.open) {
      dialog.close()
    }
  }, [isOpen])

  function handleCancel(e: React.SyntheticEvent): void {
    e.preventDefault()
    onCancel()
  }

  function handleBackdropClick(e: React.MouseEvent<HTMLDialogElement>): void {
    if (e.target === dialogRef.current) {
      onCancel()
    }
  }

  function handleDirectoryClick(path: string): void {
    setBrowsePath(path)
  }

  function handleNavigateUp(): void {
    if (data?.parent) {
      setBrowsePath(data.parent)
    } else {
      // Navigate to drives list
      setBrowsePath('')
    }
  }

  function handleBreadcrumbClick(path: string): void {
    setBrowsePath(path)
  }

  function handleSelect(): void {
    const selectedPath = data?.currentPath ?? browsePath
    onSelect(selectedPath)
  }

  const breadcrumbs = data?.currentPath ? buildBreadcrumbs(data.currentPath) : []
  const isAtRoot = !data?.currentPath
  const currentDisplay = data?.currentPath || 'Drives'

  return (
    // biome-ignore lint/a11y/useKeyWithClickEvents: native dialog handles Escape via onCancel; onClick is for backdrop dismiss only
    <dialog
      ref={dialogRef}
      className="wm-dialog-overlay"
      aria-labelledby="directory-browser-title"
      onCancel={handleCancel}
      onClick={handleBackdropClick}
    >
      <div className="wm-dialog wm-directory-browser">
        <div className="wm-dialog__title" id="directory-browser-title">
          Browse Directory
        </div>

        {/* Breadcrumb navigation */}
        <div className="wm-directory-browser__nav">
          <button
            type="button"
            className="wm-directory-browser__up-btn"
            onClick={handleNavigateUp}
            disabled={isAtRoot}
            aria-label="Navigate to parent directory"
          >
            ..
          </button>
          {breadcrumbs.length > 0 ? (
            <div className="wm-directory-browser__breadcrumbs">
              {breadcrumbs.map((crumb, index) => (
                <span key={crumb.path} className="wm-directory-browser__crumb-wrapper">
                  {index > 0 && <span className="wm-directory-browser__separator">/</span>}
                  <button
                    type="button"
                    className="wm-directory-browser__crumb"
                    onClick={() => handleBreadcrumbClick(crumb.path)}
                  >
                    {crumb.label}
                  </button>
                </span>
              ))}
            </div>
          ) : (
            <span className="wm-directory-browser__current">{currentDisplay}</span>
          )}
        </div>

        {/* Directory listing */}
        <div className="wm-directory-browser__list" aria-label="Directory listing">
          {isLoading && (
            <div className="wm-directory-browser__loading">
              <Spinner />
            </div>
          )}

          {error && (
            <div className="wm-directory-browser__error">
              {error instanceof Error ? error.message : 'Failed to load directories'}
            </div>
          )}

          {!isLoading && !error && data?.directories.length === 0 && (
            <EmptyState title="No subdirectories" description="This directory has no subdirectories." />
          )}

          {!isLoading &&
            !error &&
            data?.directories.map((dir) => (
              <button
                key={dir.path}
                type="button"
                className="wm-directory-browser__item"
                onClick={() => handleDirectoryClick(dir.path)}
              >
                <span className="wm-directory-browser__folder-icon" aria-hidden="true">
                  /
                </span>
                <span className="wm-directory-browser__item-name">{dir.name}</span>
              </button>
            ))}
        </div>

        {/* Actions */}
        <div className="wm-dialog__actions">
          <ActionButton onClick={onCancel}>Cancel</ActionButton>
          <ActionButton variant="amber" onClick={handleSelect} disabled={isLoading}>
            Select
          </ActionButton>
        </div>
      </div>
    </dialog>
  )
}

export { DirectoryBrowser, buildBreadcrumbs }
export type { DirectoryBrowserProps }
