import type { components } from './schema'

// ADR 0014: every shape here is the backend's. Nothing in this file describes
// what the API answers — it only says which of the generated types to expect.
export type AccessState = components['schemas']['AccessState']
export type ConfigurationState = components['schemas']['ConfigurationState']
export type SourceState = components['schemas']['SourceState']
export type LayoutOption = components['schemas']['LayoutOption']
export type ScanState = components['schemas']['ScanState']
export type ScannedFileState = components['schemas']['ScannedFileState']
export type ScannedSourceState = components['schemas']['ScannedSourceState']
export type RecognisedState = components['schemas']['RecognisedState']

type ConfigurationProblem = components['schemas']['ConfigurationProblem']
type ProblemResponse = components['schemas']['ProblemResponse']

/**
 * The API answered and said no. `configuration` is what is still stored, which
 * the configuration endpoints send along with every refusal so the screen never
 * has to guess whether the change went through.
 */
export class Refused extends Error {
  readonly configuration: ConfigurationState | undefined

  constructor(message: string, configuration?: ConfigurationState) {
    super(message)
    this.name = 'Refused'
    this.configuration = configuration
  }
}

/** The session is gone — expired, revoked, or the container was reset. */
export class SignedOut extends Error {
  constructor() {
    super('Signed out.')
    this.name = 'SignedOut'
  }
}

async function request<T>(
  path: string,
  init: RequestInit = {},
  // The sign-in endpoints answer 401 to mean "wrong password", which is a
  // message next to the field rather than a session that has ended.
  unauthorizedMeansSignedOut = true,
): Promise<T> {
  let response: Response

  try {
    response = await fetch(path, {
      ...init,
      headers: init.body === undefined ? undefined : { 'content-type': 'application/json' },
    })
  } catch {
    throw new Refused('The tool did not answer. It may be restarting — try again in a moment.')
  }

  if (response.status === 401 && unauthorizedMeansSignedOut) {
    throw new SignedOut()
  }

  const body = await readJson(response)

  if (!response.ok) {
    const problem = body as ConfigurationProblem & ProblemResponse
    throw new Refused(problem?.message ?? `The tool answered ${response.status}.`, problem?.configuration)
  }

  return body as T
}

async function readJson(response: Response): Promise<unknown> {
  if (response.status === 204) {
    return undefined
  }

  try {
    return await response.json()
  } catch {
    return undefined
  }
}

const body = (value: unknown) => JSON.stringify(value)

export const access = {
  state: () => request<AccessState>('/api/access/state', {}, false),

  setPassword: (password: string) =>
    request<AccessState>('/api/access/password', { method: 'POST', body: body({ password }) }, false),

  signIn: (password: string) =>
    request<AccessState>('/api/access/session', { method: 'POST', body: body({ password }) }, false),

  signOut: () => request<void>('/api/access/session', { method: 'DELETE' }),
}

export const configuration = {
  read: () => request<ConfigurationState>('/api/configuration'),

  setApiKey: (apiKey: string) =>
    request<ConfigurationState>('/api/configuration/api-key', { method: 'PUT', body: body({ apiKey }) }),

  addSource: (path: string) =>
    request<ConfigurationState>('/api/configuration/sources', { method: 'POST', body: body({ path }) }),

  removeSource: (id: SourceState['id']) =>
    request<ConfigurationState>(`/api/configuration/sources/${String(id)}`, { method: 'DELETE' }),

  setTarget: (path: string, layout: string) =>
    request<ConfigurationState>('/api/configuration/target', { method: 'PUT', body: body({ path, layout }) }),

  finish: () => request<ConfigurationState>('/api/configuration/completion', { method: 'POST' }),
}

export const scanning = {
  read: () => request<ScanState>('/api/scan'),

  /**
   * Asks for a scan. It answers as soon as one is under way rather than when it
   * has finished — a first pass over an existing library takes minutes — so the
   * screen keeps reading `read` until `scanning` goes back to false.
   */
  now: () => request<ScanState>('/api/scan', { method: 'POST' }),

  /**
   * Asks prdb about whatever has finished downloading. Like a scan, it answers
   * as soon as a run is under way rather than when it has finished — a library
   * is a few minutes of batches — so the screen keeps reading `read` until
   * `identification.running` goes back to false.
   */
  identify: () => request<ScanState>('/api/identification', { method: 'POST' }),
}
