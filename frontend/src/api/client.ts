import { emitChange } from '@/hooks/use-auth'
import { API_BASE, AUTH_STORAGE_KEY } from '@/lib/constants'

class ApiError extends Error {
  constructor(
    public statusCode: number,
    public body: string,
  ) {
    super(`API ${statusCode}: ${body}`)
    this.name = 'ApiError'
  }
}

function getAuthHeaders(): Record<string, string> {
  const key = localStorage.getItem(AUTH_STORAGE_KEY)
  if (key) {
    return { 'X-User-Key': key }
  }
  return {}
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...getAuthHeaders(),
      ...options?.headers,
    },
  })

  if (res.status === 401) {
    // Key is invalid or deactivated — clear session and surface AuthGate
    localStorage.removeItem(AUTH_STORAGE_KEY)
    emitChange()
    throw new ApiError(401, 'Session expired. Please log in again.')
  }

  if (!res.ok) {
    const body = await res.text()
    throw new ApiError(res.status, body)
  }

  if (res.status === 204) {
    return undefined as T
  }

  return res.json() as Promise<T>
}

export { request, ApiError }
