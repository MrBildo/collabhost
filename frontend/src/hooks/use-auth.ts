import { AUTH_STORAGE_KEY } from '@/lib/constants'
import { useCallback, useSyncExternalStore } from 'react'

let listeners: Array<() => void> = []

function emitChange(): void {
  for (const listener of listeners) {
    listener()
  }
}

function subscribe(listener: () => void): () => void {
  listeners = [...listeners, listener]

  // Cross-tab propagation (FE-AUTH-04): the browser fires a `storage` event in
  // OTHER tabs when localStorage changes. Same-tab login/logout already notifies
  // via emitChange(); without this, a logout (or login) in one tab leaves the
  // others stuck on the old session until reload. We only re-read the snapshot
  // when our auth key changed (`key === null` covers a full clear()).
  function handleStorage(event: StorageEvent): void {
    if (event.key === AUTH_STORAGE_KEY || event.key === null) {
      listener()
    }
  }
  window.addEventListener('storage', handleStorage)

  return () => {
    listeners = listeners.filter((l) => l !== listener)
    window.removeEventListener('storage', handleStorage)
  }
}

function getSnapshot(): string | null {
  return localStorage.getItem(AUTH_STORAGE_KEY)
}

function useAuth(): {
  userKey: string | null
  isAuthenticated: boolean
  login: (key: string) => void
  logout: () => void
} {
  const userKey = useSyncExternalStore(subscribe, getSnapshot)

  const login = useCallback((key: string) => {
    localStorage.setItem(AUTH_STORAGE_KEY, key)
    emitChange()
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem(AUTH_STORAGE_KEY)
    emitChange()
  }, [])

  return {
    userKey,
    isAuthenticated: userKey !== null && userKey.length > 0,
    login,
    logout,
  }
}

export { useAuth, emitChange }
