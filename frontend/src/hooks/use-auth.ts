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
  return () => {
    listeners = listeners.filter((l) => l !== listener)
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

export { useAuth }
