import { beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('./client', () => ({
  request: vi.fn().mockResolvedValue(undefined),
}))

import { request } from './client'
import {
  deactivateUser,
  deleteApp,
  fetchUser,
  getAppDetail,
  getAppLogs,
  getAppSettings,
  getRegistrationSchema,
  importRuntimeConfigFile,
  killApp,
  restartApp,
  startApp,
  stopApp,
  updateAppSettings,
} from './endpoints'

const requestMock = vi.mocked(request)

function requestedPath(): string {
  const call = requestMock.mock.calls[requestMock.mock.calls.length - 1]
  if (!call) throw new Error('request was not called')
  return call[0]
}

beforeEach(() => {
  requestMock.mockClear()
})

describe('endpoints keep route params inside their own path segment', () => {
  // A slug or id from the address bar may carry `/`, `?` or `#` once React Router
  // decodes it. Each case below would reach a different endpoint if pasted raw.
  const crafted = '../users/01ABC/deactivate?'
  const encoded = '..%2Fusers%2F01ABC%2Fdeactivate%3F'

  test.each([
    ['getAppDetail', () => getAppDetail(crafted), `/apps/${encoded}`],
    ['startApp', () => startApp(crafted), `/apps/${encoded}/start`],
    ['stopApp', () => stopApp(crafted), `/apps/${encoded}/stop`],
    ['restartApp', () => restartApp(crafted), `/apps/${encoded}/restart`],
    ['killApp', () => killApp(crafted), `/apps/${encoded}/kill`],
    ['deleteApp', () => deleteApp(crafted), `/apps/${encoded}`],
    ['getAppSettings', () => getAppSettings(crafted), `/apps/${encoded}/settings`],
    ['updateAppSettings', () => updateAppSettings(crafted, { changes: {} }), `/apps/${encoded}/settings`],
    ['importRuntimeConfigFile', () => importRuntimeConfigFile(crafted), `/apps/${encoded}/runtime-config-file/import`],
    ['getAppLogs', () => getAppLogs(crafted, { lines: 10 }), `/apps/${encoded}/logs?lines=10`],
    ['getRegistrationSchema', () => getRegistrationSchema(crafted), `/app-types/${encoded}/registration`],
    ['fetchUser', () => fetchUser(crafted), `/users/${encoded}`],
    ['deactivateUser', () => deactivateUser(crafted), `/users/${encoded}/deactivate`],
  ])('%s encodes its route param', async (_name, call, expectedPath) => {
    await call()
    expect(requestedPath()).toBe(expectedPath)
  })

  test('an ordinary slug produces the same path as before', async () => {
    await startApp('my-app')
    expect(requestedPath()).toBe('/apps/my-app/start')
  })

  test('a dot-segment slug is refused before any request is sent', () => {
    expect(() => getAppDetail('..')).toThrow('not a valid path segment')
    expect(requestMock).not.toHaveBeenCalled()
  })
})
