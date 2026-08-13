import { useCallback, useState } from 'react'

import {
  configuration as api,
  Refused,
  SignedOut,
  type ConfigurationState,
  type SourceState,
} from '../api/client'

/** Runs one change and reports what the tool said about it, or null if it agreed. */
type Run = (call: () => Promise<ConfigurationState>) => Promise<string | null>

/**
 * The guided path of ADR 0009, and the settings page afterwards — one screen,
 * because it is one configuration. Before onboarding is finished the steps
 * appear one at a time; after it, they are all just fields that can be changed.
 *
 * The configuration arrives from the workspace around it rather than being
 * fetched here: finishing the setup is what puts the rest of the tool within
 * reach, so the answer has to be shared with whatever draws the navigation.
 */
export default function ConfigurationScreen({
  initial,
  onChanged,
  onSignedOut,
}: {
  initial: ConfigurationState
  onChanged: (state: ConfigurationState) => void
  onSignedOut: () => void
}) {
  const [state, setState] = useState<ConfigurationState>(initial)

  const run = useCallback<Run>(
    async (call) => {
      try {
        const next = await call()
        setState(next)
        onChanged(next)

        return null
      } catch (error) {
        if (error instanceof SignedOut) {
          onSignedOut()
          return null
        }

        if (error instanceof Refused) {
          // A refusal carries the configuration as it still stands, so the
          // screen stays true to the tool even while it shows the complaint.
          if (error.configuration !== undefined) {
            setState(error.configuration)
            onChanged(error.configuration)
          }

          return error.message
        }

        return 'Something went wrong.'
      }
    },
    [onChanged, onSignedOut],
  )

  // While the path is being walked, a step appears once the one before it has
  // been answered. Afterwards there is no path left to walk, only settings.
  // Cumulative, not one condition per step: a configuration that was filled in
  // out of order — through the API, or by a key that stopped working — would
  // otherwise show step three while step two is still hidden.
  const guided = !state.complete
  const showSources = !guided || state.apiKeySet
  const showTarget = showSources && (!guided || state.sources.length > 0)

  return (
    <>
      <p className={state.readyToComplete ? 'summary' : 'summary waiting'}>{state.whatHappensNext}</p>

      <ApiKeyStep state={state} run={run} />
      {showSources && <SourcesStep state={state} run={run} />}
      {showTarget && <TargetStep state={state} run={run} />}
      {guided && <FinishStep state={state} run={run} />}
    </>
  )
}

function ApiKeyStep({ state, run }: { state: ConfigurationState; run: Run }) {
  const [apiKey, setApiKey] = useState('')
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setProblem(await run(() => api.setApiKey(apiKey)))
    setBusy(false)
    setApiKey('')
  }

  return (
    <Step number={1} title="Your prdb API key" done={state.apiKeySet}>
      <p className="hint">
        prdb is where the tool gets what it knows about a video. The key is on your account page
        at prdb.net — it is checked here before it is stored, and it never leaves this container
        again.
      </p>

      {/* ADR 0001: this is not optional and not anonymous, because it is what
          identification is. It belongs here, before the key is stored, rather
          than in a changelog somebody finds afterwards. */}
      <p className="hint">
        To work out what a file is, the tool sends its name, its size and a hash of it to prdb —
        for every file it examines. The files themselves are never uploaded.
      </p>

      {state.apiKeySet && <p className="done">A key is stored and prdb accepted it.</p>}

      <form onSubmit={submit}>
        <label>
          {state.apiKeySet ? 'Replace it' : 'API key'}
          <input
            type="password"
            autoComplete="off"
            spellCheck={false}
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
          />
        </label>

        {problem !== null && <p className="problem">{problem}</p>}

        <button type="submit" disabled={busy || apiKey.trim().length === 0}>
          {busy ? 'Asking prdb…' : 'Check and save'}
        </button>
      </form>
    </Step>
  )
}

function SourcesStep({ state, run }: { state: ConfigurationState; run: Run }) {
  const [path, setPath] = useState('')
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const add = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy(true)

    const refusal = await run(() => api.addSource(path))
    setProblem(refusal)
    setBusy(false)

    if (refusal === null) {
      setPath('')
    }
  }

  const remove = async (id: SourceState['id']) => {
    setProblem(await run(() => api.removeSource(id)))
  }

  return (
    <Step number={2} title="Where your downloads arrive" done={state.sources.length > 0}>
      <p className="hint">
        The paths as the container sees them — the right-hand side of each volume, not the path
        on the NAS. There can be several.
      </p>

      {state.sources.length > 0 && (
        <ul className="directories">
          {state.sources.map((source) => (
            <li key={String(source.id)}>
              <div className="row">
                <code>{source.path}</code>
                <button type="button" className="quiet" onClick={() => void remove(source.id)}>
                  Remove
                </button>
              </div>
              {source.usable ? (
                <p className="hint">{source.movementExplained}</p>
              ) : (
                <p className="problem">{source.problem}</p>
              )}
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={add}>
        <label>
          Download directory
          <input
            type="text"
            placeholder="/downloads"
            spellCheck={false}
            value={path}
            onChange={(event) => setPath(event.target.value)}
          />
        </label>

        {problem !== null && <p className="problem">{problem}</p>}

        <button type="submit" disabled={busy || path.trim().length === 0}>
          {busy ? 'Looking…' : 'Add'}
        </button>
      </form>
    </Step>
  )
}

function TargetStep({ state, run }: { state: ConfigurationState; run: Run }) {
  const [path, setPath] = useState(state.target?.path ?? '')
  const [layout, setLayout] = useState(state.layout ?? state.availableLayouts[0]?.name ?? '')
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setProblem(await run(() => api.setTarget(path, layout)))
    setBusy(false)
  }

  const chosen = state.availableLayouts.find((option) => option.name === layout)

  return (
    <Step number={3} title="Where your library lives" done={state.target?.usable === true}>
      <p className="hint">
        Videos are moved here once they have been recognised, in the shape your media server
        reads. Keep it out of the download directories, or the tool will find its own work.
      </p>

      {state.target !== null && state.target.usable !== true && (
        <p className="problem">{state.target.problem}</p>
      )}

      <form onSubmit={submit}>
        <label>
          Library directory
          <input
            type="text"
            placeholder="/library"
            spellCheck={false}
            value={path}
            onChange={(event) => setPath(event.target.value)}
          />
        </label>

        <label>
          Media server
          <select value={layout} onChange={(event) => setLayout(event.target.value)}>
            {state.availableLayouts.map((option) => (
              <option key={option.name} value={option.name}>
                {option.name}
              </option>
            ))}
          </select>
        </label>

        {chosen !== undefined && <p className="hint">{chosen.description}</p>}

        {problem !== null && <p className="problem">{problem}</p>}

        <button type="submit" disabled={busy || path.trim().length === 0}>
          {busy ? 'Looking…' : 'Save'}
        </button>
      </form>
    </Step>
  )
}

function FinishStep({ state, run }: { state: ConfigurationState; run: Run }) {
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const finish = async () => {
    setBusy(true)
    setProblem(await run(api.finish))
    setBusy(false)
  }

  return (
    <Step number={4} title="Finish" done={state.complete}>
      <p className="hint">
        Until this is done the tool scans nothing. Everything above stays editable afterwards —
        this is the configuration, not a wizard that locks itself.
      </p>

      {problem !== null && <p className="problem">{problem}</p>}

      <button type="button" onClick={() => void finish()} disabled={busy || !state.readyToComplete}>
        {busy ? 'Finishing…' : 'Finish setup'}
      </button>
    </Step>
  )
}

function Step({
  number,
  title,
  done,
  children,
}: {
  number: number
  title: string
  done: boolean
  children: React.ReactNode
}) {
  return (
    <section className="card">
      <h2>
        <span className={done ? 'step done' : 'step'}>{done ? '✓' : number}</span>
        {title}
      </h2>
      {children}
    </section>
  )
}
