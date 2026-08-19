import type { components } from './schema'

// ADR 0014: every shape here is the backend's. Nothing in this file describes
// what the API answers — it only says which of the generated types to expect.
export type AccessState = components['schemas']['AccessState']
export type ConfigurationState = components['schemas']['ConfigurationState']
export type SourceState = components['schemas']['SourceState']
export type LayoutOption = components['schemas']['LayoutOption']
export type MediaServerState = components['schemas']['MediaServerState']
export type MediaServerCheckState = components['schemas']['MediaServerCheckState']
export type ScanState = components['schemas']['ScanState']
export type ScannedFileState = components['schemas']['ScannedFileState']
export type ScannedSourceState = components['schemas']['ScannedSourceState']
export type RecognisedState = components['schemas']['RecognisedState']
export type FilingState = components['schemas']['FilingState']
export type PlannedFileState = components['schemas']['PlannedFileState']
export type FiledFileState = components['schemas']['FiledFileState']
export type HistoryState = components['schemas']['HistoryState']
export type LoggedRunState = components['schemas']['LoggedRunState']
export type LoggedOperationState = components['schemas']['LoggedOperationState']
export type UndoState = components['schemas']['UndoState']
export type UndoPlanState = components['schemas']['UndoPlanState']
export type UndoneFileState = components['schemas']['UndoneFileState']
export type ReviewQueueState = components['schemas']['ReviewQueueState']
export type ReviewEntryState = components['schemas']['ReviewEntryState']
export type ReviewCandidateState = components['schemas']['ReviewCandidateState']
export type ReviewDecisionState = components['schemas']['ReviewDecisionState']
export type ReviewSiteState = components['schemas']['ReviewSiteState']
export type ReviewSummaryState = components['schemas']['ReviewSummaryState']
export type VideoSearchState = components['schemas']['VideoSearchState']
export type VideoState = components['schemas']['VideoState']

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

  /**
   * The one image per filed scene — ADR 0027. It is off until this is called
   * with true, and it belongs to the library settings rather than to the guided
   * path, because the tool files perfectly well without it.
   */
  setArtwork: (enabled: boolean) =>
    request<ConfigurationState>('/api/configuration/artwork', {
      method: 'PUT',
      body: body({ enabled }),
    }),

  /**
   * The optional media server connection — ADR 0018. Storing it answers with
   * what the server said about itself, which is more than "it answered": the
   * release date format, and whether it holds anything this tool has filed.
   */
  setMediaServer: (url: string, apiKey: string) =>
    request<MediaServerCheckState>('/api/configuration/media-server', {
      method: 'PUT',
      body: body({ url, apiKey }),
    }),

  testMediaServer: () =>
    request<MediaServerCheckState>('/api/configuration/media-server/test', { method: 'POST' }),

  forgetMediaServer: () =>
    request<ConfigurationState>('/api/configuration/media-server', { method: 'DELETE' }),

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

/**
 * Nothing here moves a file except `file`, and nothing moves one without it —
 * ADR 0022. `plan` is what the user reads first, and both answer as soon as a
 * run is under way rather than when it has finished, so the screen keeps
 * reading `read` until `running` goes back to false.
 */
export const filing = {
  read: () => request<FilingState>('/api/filing'),

  plan: () => request<FilingState>('/api/filing/plan', { method: 'POST' }),

  file: () => request<FilingState>('/api/filing', { method: 'POST' }),
}

/**
 * What the tool did to somebody's files, and the way back out of it — ADR 0028
 * and ADR 0029.
 *
 * `check` moves nothing and `undo` moves files back; they are two calls for the
 * reason filing's two are, and the second is the one that is not safe to press.
 * Both answer as soon as a run is under way rather than when it has finished, so
 * the screen keeps reading `undoState` until `running` goes back to false.
 */
export const history = {
  read: (page = 1) => request<HistoryState>(`/api/history?page=${String(page)}`),

  undoState: () => request<UndoState>('/api/history/undo'),

  checkRun: (runId: number) =>
    request<UndoState>(`/api/history/runs/${String(runId)}/undo/check`, { method: 'POST' }),

  undoRun: (runId: number) =>
    request<UndoState>(`/api/history/runs/${String(runId)}/undo`, { method: 'POST' }),

  checkOperation: (operationId: number) =>
    request<UndoState>(`/api/history/operations/${String(operationId)}/undo/check`, {
      method: 'POST',
    }),

  undoOperation: (operationId: number) =>
    request<UndoState>(`/api/history/operations/${String(operationId)}/undo`, { method: 'POST' }),
}

/** Which list of the queue is being read. */
export type ReviewFilter = 'waiting' | 'assigned' | 'dismissed'

/**
 * The queue moves no files. Everything here writes down what a file *is* —
 * ADR 0023 — and filing is still the run somebody asks for afterwards.
 *
 * A refusal comes back as a 400 carrying the same shape as an answer, so
 * `Refused` has the message and the screen keeps its counts.
 */
export const review = {
  read: (filter: ReviewFilter = 'waiting', site?: string, noSite = false, page = 1) => {
    const query = new URLSearchParams({ filter, page: String(page) })

    if (site !== undefined) {
      query.set('site', site)
    }

    if (noSite) {
      query.set('noSite', 'true')
    }

    return request<ReviewQueueState>(`/api/queue?${query.toString()}`)
  },

  /** A request against the user's prdb quota, spent because they typed something. */
  search: (q: string, site?: string) => {
    const query = new URLSearchParams({ q })

    if (site !== undefined) {
      query.set('site', site)
    }

    return request<VideoSearchState>(`/api/queue/search?${query.toString()}`)
  },

  assign: (fileId: number, videoId: string) =>
    request<ReviewDecisionState>(`/api/queue/${String(fileId)}/assignment`, {
      method: 'POST',
      body: body({ videoId }),
    }),

  dismiss: (fileId: number) =>
    request<ReviewDecisionState>(`/api/queue/${String(fileId)}/dismissal`, { method: 'POST' }),

  dismissMany: (fileIds: number[]) =>
    request<ReviewDecisionState>('/api/queue/dismissals', {
      method: 'POST',
      body: body({ fileIds }),
    }),

  /** The way back from a wrong button, for either kind of decision. */
  forget: (fileId: number) =>
    request<ReviewDecisionState>(`/api/queue/${String(fileId)}/decision`, { method: 'DELETE' }),
}
