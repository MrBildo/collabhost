const API_BASE = '/api/v1'

const BASE_DOMAIN = 'collab.internal'

const POLL_INTERVALS = {
  apps: 5_000,
  appDetail: 3_000,
  dashboard: 5_000,
  logs: 2_000,
  system: 30_000,
} as const

const AUTH_STORAGE_KEY = 'collabhost-user-key'

const LOG_BUFFER_CAP = 1_000

export { API_BASE, BASE_DOMAIN, POLL_INTERVALS, AUTH_STORAGE_KEY, LOG_BUFFER_CAP }
