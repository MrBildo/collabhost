import { beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('./client', () => ({
  request: vi.fn().mockResolvedValue(undefined),
}))

import { request } from './client'
import * as endpoints from './endpoints'

const requestMock = vi.mocked(request)

function requestedPath(): string {
  const call = requestMock.mock.calls[requestMock.mock.calls.length - 1]
  if (!call) throw new Error('request was not called')
  return call[0]
}

type PathParamCase = [name: string, call: (value: string) => Promise<unknown>, pathFor: (segment: string) => string]

// Every endpoint that puts a caller-supplied value into the URL path.
const PATH_PARAM_CASES: PathParamCase[] = [
  ['getAppDetail', (v) => endpoints.getAppDetail(v), (s) => `/apps/${s}`],
  ['startApp', (v) => endpoints.startApp(v), (s) => `/apps/${s}/start`],
  ['stopApp', (v) => endpoints.stopApp(v), (s) => `/apps/${s}/stop`],
  ['restartApp', (v) => endpoints.restartApp(v), (s) => `/apps/${s}/restart`],
  ['killApp', (v) => endpoints.killApp(v), (s) => `/apps/${s}/kill`],
  ['deleteApp', (v) => endpoints.deleteApp(v), (s) => `/apps/${s}`],
  ['getAppSettings', (v) => endpoints.getAppSettings(v), (s) => `/apps/${s}/settings`],
  ['updateAppSettings', (v) => endpoints.updateAppSettings(v, { changes: {} }), (s) => `/apps/${s}/settings`],
  [
    'importRuntimeConfigFile',
    (v) => endpoints.importRuntimeConfigFile(v),
    (s) => `/apps/${s}/runtime-config-file/import`,
  ],
  ['getAppLogs', (v) => endpoints.getAppLogs(v, { lines: 10 }), (s) => `/apps/${s}/logs?lines=10`],
  ['getRegistrationSchema', (v) => endpoints.getRegistrationSchema(v), (s) => `/app-types/${s}/registration`],
  ['fetchUser', (v) => endpoints.fetchUser(v), (s) => `/users/${s}`],
  ['deactivateUser', (v) => endpoints.deactivateUser(v), (s) => `/users/${s}/deactivate`],
]

// Endpoints whose path is fixed. Query-string values are encoded by URLSearchParams
// or encodeURIComponent inside the function, not by the path helper.
const NO_PATH_PARAM = [
  'browseFilesystem',
  'createApp',
  'createUser',
  'detectStrategy',
  'fetchMe',
  'fetchUsers',
  'getAppTypes',
  'getApps',
  'getDashboardEvents',
  'getDashboardStats',
  'getRoutes',
  'getSystemStatus',
  'reloadProxy',
]

beforeEach(() => {
  requestMock.mockClear()
})

describe('endpoints keep route params inside their own path segment', () => {
  // A slug or id from the address bar may carry `/`, `?` or `#` once React Router
  // decodes it. Each case below would reach a different endpoint if pasted raw.
  const crafted = '../users/01ABC/deactivate?'
  const encoded = '..%2Fusers%2F01ABC%2Fdeactivate%3F'

  test('every endpoint export is classified as taking a path parameter or not', () => {
    // A new endpoint fails here until it is added to one of the two lists, and the
    // path-parameter list is what the encoding tests below run over.
    const classified = [...PATH_PARAM_CASES.map(([name]) => name), ...NO_PATH_PARAM].sort()
    expect(Object.keys(endpoints).sort()).toEqual(classified)
  })

  test.each(PATH_PARAM_CASES)('%s encodes its route param', async (_name, call, pathFor) => {
    await call(crafted)
    expect(requestedPath()).toBe(pathFor(encoded))
  })

  test.each(PATH_PARAM_CASES)('%s rejects a dot-segment value without sending a request', async (_name, call) => {
    await expect(call('..')).rejects.toThrow('not a valid path segment')
    expect(requestMock).not.toHaveBeenCalled()
  })

  test('an ordinary slug produces the same path as before', async () => {
    await endpoints.startApp('my-app')
    expect(requestedPath()).toBe('/apps/my-app/start')
  })
})
