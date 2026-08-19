import { useCallback, useState } from 'react'

import {
  configuration as api,
  Refused,
  SignedOut,
  type ConfigurationState,
  type MediaServerCheckState,
  type SourceState,
} from '../api/client'
import Navigation from '../navigation/Navigation'
import { firstSection, sections, type SectionPath } from './sections'

/** Runs one change and reports what the tool said about it, or null if it agreed. */
type Run = (call: () => Promise<ConfigurationState>) => Promise<string | null>

/**
 * The same for the media server, which answers with more than a yes: a
 * connection can be stored and still have something worth reading next to it.
 * A string is a refusal, and null is nothing to say.
 */
type RunCheck = (
  call: () => Promise<MediaServerCheckState>,
) => Promise<MediaServerCheckState | string | null>

/**
 * The guided path of ADR 0009, and the settings page afterwards — one screen,
 * because it is one configuration. The two halves are shaped by what somebody
 * is doing in them, and that is why they are drawn differently below.
 *
 * Walking the path, the order is the whole point: a step appears once the one
 * before it has been answered, all of them under each other, numbered, so that
 * what is left to do is visible without clicking anything.
 *
 * Afterwards there is no order left. Somebody who opens the settings came to
 * change one thing, so the four blocks become four sections with an address
 * each and one of them on screen — see `sections.ts`. The alternative is the
 * column, and a column only grows: the switches around duplicates and
 * contributions ADR 0009 promises would land at the bottom of it.
 *
 * The configuration arrives from the workspace around it rather than being
 * fetched here: finishing the setup is what puts the rest of the tool within
 * reach, so the answer has to be shared with whatever draws the navigation.
 */
export default function ConfigurationScreen({
  initial,
  section,
  onSection,
  onChanged,
  onSignedOut,
}: {
  initial: ConfigurationState
  section: SectionPath | null
  onSection: (section: SectionPath) => void
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

  const runCheck = useCallback<RunCheck>(
    async (call) => {
      try {
        const answer = await call()
        setState(answer.configuration)
        onChanged(answer.configuration)

        return answer
      } catch (error) {
        if (error instanceof SignedOut) {
          onSignedOut()
          return null
        }

        if (error instanceof Refused) {
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

  const summary = (
    <p className={state.readyToComplete ? 'summary' : 'summary waiting'}>{state.whatHappensNext}</p>
  )

  if (!state.complete) {
    // A step appears once the one before it has been answered. Cumulative, not
    // one condition per step: a configuration that was filled in out of order —
    // through the API, or by a key that stopped working — would otherwise show
    // step three while step two is still hidden.
    const showSources = state.apiKeySet
    const showTarget = showSources && state.sources.length > 0
    const showMediaServer = showTarget && state.target?.usable === true

    return (
      <>
        {summary}

        <ApiKeyStep state={state} run={run} number={1} />
        {showSources && <SourcesStep state={state} run={run} number={2} />}
        {showTarget && <TargetStep state={state} run={run} number={3} />}
        {showMediaServer && <MediaServerStep state={state} run={run} runCheck={runCheck} number={4} />}
        <FinishStep state={state} run={run} />
      </>
    )
  }

  // The address catches up a render later — the workspace replaces a bare
  // `/settings` with the first section — so the screen answers for itself in
  // the meantime rather than showing nothing at all for a frame.
  const chosen = section ?? firstSection

  return (
    <>
      <Navigation
        kind="section"
        label="Settings"
        links={sections.map((one) => ({
          to: one.path,
          href: `/settings/${one.path}`,
          label: one.label,
        }))}
        chosen={chosen}
        onChosen={onSection}
      />

      {summary}

      {chosen === 'prdb' && <ApiKeyStep state={state} run={run} />}
      {chosen === 'sources' && <SourcesStep state={state} run={run} />}
      {chosen === 'library' && <TargetStep state={state} run={run} />}
      {chosen === 'media-server' && <MediaServerStep state={state} run={run} runCheck={runCheck} />}
    </>
  )
}

function ApiKeyStep({
  state,
  run,
  number,
}: {
  state: ConfigurationState
  run: Run
  number?: number
}) {
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
    <Step number={number} title="Your prdb API key" done={state.apiKeySet}>
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

function SourcesStep({
  state,
  run,
  number,
}: {
  state: ConfigurationState
  run: Run
  number?: number
}) {
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
    <Step number={number} title="Where your downloads arrive" done={state.sources.length > 0}>
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

function TargetStep({
  state,
  run,
  number,
}: {
  state: ConfigurationState
  run: Run
  number?: number
}) {
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
    <Step number={number} title="Where your library lives" done={state.target?.usable === true}>
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

      {/* Not part of the guided path — ADR 0027. Onboarding collects what the
          tool cannot run without, and it runs without this; a fifth step for a
          switch would make an optional thing look like a missing answer. */}
      {number === undefined && <ArtworkSwitch state={state} run={run} />}
    </Step>
  )
}

/**
 * One image per filed scene, off until somebody says otherwise — ADR 0027. It
 * lives here rather than in a section of its own because it is a property of
 * what filing writes into this directory.
 *
 * It saves as it is clicked, unlike the forms above it: there is nothing to
 * type, nothing to check, and nothing happens until the next filing run, so a
 * Save button next to a checkbox would be a second click for no second decision.
 */
function ArtworkSwitch({ state, run }: { state: ConfigurationState; run: Run }) {
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const toggle = async (enabled: boolean) => {
    setBusy(true)
    setProblem(await run(() => api.setArtwork(enabled)))
    setBusy(false)
  }

  return (
    <>
      <label className="pick">
        <input
          type="checkbox"
          checked={state.artwork}
          disabled={busy}
          onChange={(event) => void toggle(event.target.checked)}
        />
        Download one image for each scene
      </label>

      <p className="hint">
        With this on, a filed scene gets a <code>fanart.jpg</code> next to it, downloaded from
        prdb — one image, and only where there is no file at that name. Nothing is ever written
        over: deleting the file is how you ask for a fresh one. It costs a download per scene
        filed, which is why it is off unless you turn it on.
      </p>

      {problem !== null && <p className="problem">{problem}</p>}
    </>
  )
}

/**
 * ADR 0018's two optional fields. Everything about this step says so: it can be
 * walked past, the setup finishes without it, and nothing on the screen calls a
 * blank one a problem — leaving it empty is what most installations do.
 *
 * What it is not is a reachability check. The test reads back the one server
 * setting that would silently discard every date the tool writes, and looks for
 * something the tool has filed; a server that answers and holds none of it looks
 * fine and does nothing, so that is said here rather than nowhere.
 */
function MediaServerStep({
  state,
  run,
  runCheck,
  number,
}: {
  state: ConfigurationState
  run: Run
  runCheck: RunCheck
  number?: number
}) {
  const [url, setUrl] = useState(state.mediaServer?.url ?? '')
  const [apiKey, setApiKey] = useState('')
  const [check, setCheck] = useState<MediaServerCheckState | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const answered = (result: MediaServerCheckState | string | null) => {
    setCheck(typeof result === 'object' ? result : null)
    setProblem(typeof result === 'string' ? result : null)
  }

  const save = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy(true)
    answered(await runCheck(() => api.setMediaServer(url, apiKey)))
    setBusy(false)
    setApiKey('')
  }

  const test = async () => {
    setBusy(true)
    answered(await runCheck(api.testMediaServer))
    setBusy(false)
  }

  const forget = async () => {
    setBusy(true)
    setCheck(null)
    setProblem(await run(api.forgetMediaServer))
    setBusy(false)
    setUrl('')
    setApiKey('')
  }

  const connected = state.mediaServer !== null

  return (
    <Step number={number} title="Your media server (optional)" done={connected}>
      <p className="hint">
        Leave this empty and everything still works: the tool files videos and writes the metadata
        file next to each one, and your media server picks them up on its next scan. Fill it in and
        two more things happen — a video you file shows up there straight away instead of on the
        next scan, and the setup can tell you now whether the server will read the dates the tool
        writes.
      </p>

      {connected && <p className="done">Connected to {state.mediaServer?.url}</p>}

      {check !== null && <p className={check.working ? 'done' : 'problem'}>{check.message}</p>}

      <form onSubmit={save}>
        <label>
          Media server address
          <input
            type="text"
            placeholder="http://192.168.1.10:8096"
            spellCheck={false}
            value={url}
            onChange={(event) => setUrl(event.target.value)}
          />
        </label>

        <label>
          API key
          <input
            type="password"
            autoComplete="off"
            spellCheck={false}
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
          />
        </label>

        <p className="hint">
          In Jellyfin the key is made under Dashboard → API keys. It needs no user name and no
          password, and it is checked here before it is stored.
        </p>

        {problem !== null && <p className="problem">{problem}</p>}

        <button type="submit" disabled={busy || url.trim().length === 0 || apiKey.trim().length === 0}>
          {busy ? 'Asking…' : 'Check and save'}
        </button>

        {connected && (
          <>
            <button type="button" className="quiet" onClick={() => void test()} disabled={busy}>
              Test again
            </button>
            <button type="button" className="quiet" onClick={() => void forget()} disabled={busy}>
              Forget it
            </button>
          </>
        )}
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
    <Step number={5} title="Finish" done={state.complete}>
      <p className="hint">
        Until this is done the tool scans nothing. Everything above stays editable afterwards —
        this is the configuration, not a wizard that locks itself. Step 4 can be left empty; the
        button below does not wait for it.
      </p>

      {problem !== null && <p className="problem">{problem}</p>}

      <button type="button" onClick={() => void finish()} disabled={busy || !state.readyToComplete}>
        {busy ? 'Finishing…' : 'Finish setup'}
      </button>
    </Step>
  )
}

/**
 * One block of the configuration, numbered while it is a step of the guided
 * path and plain once it is a section of the settings — a number is an answer
 * to "how much is left", and on a page showing one block there is no such
 * question. What each block says about its own state it says in its prose.
 */
function Step({
  number,
  title,
  done,
  children,
}: {
  number?: number
  title: string
  done: boolean
  children: React.ReactNode
}) {
  return (
    <section className="card">
      <h2>
        {number !== undefined && (
          <span className={done ? 'step done' : 'step'}>{done ? '✓' : number}</span>
        )}
        {title}
      </h2>
      {children}
    </section>
  )
}
