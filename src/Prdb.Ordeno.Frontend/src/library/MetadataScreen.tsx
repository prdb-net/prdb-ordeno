import { useCallback, useEffect, useRef, useState } from 'react'

import {
  Refused,
  SignedOut,
  refresh as api,
  type RefreshState,
  type RefreshedSceneState,
} from '../api/client'
import RowTable, { Row } from '../ui/RowTable'

/** How often the screen asks again while a run is under way. */
const WhileWorking = 2000

/**
 * What the tool has already filed, checked against what prdb says now —
 * ADR 0032.
 *
 * One button and no preview, which is the difference between this area and the
 * Filing one: nothing here moves a file, and working out what it would write
 * costs exactly the prdb requests that writing it costs. The run reports what it
 * did, and the History has the run.
 */
export default function MetadataScreen({ onSignedOut }: { onSignedOut: () => void }) {
  const [state, setState] = useState<RefreshState | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const read = useCallback(
    async (call: () => Promise<RefreshState>) => {
      try {
        setState(await call())
        setProblem(null)
      } catch (error) {
        if (error instanceof SignedOut) {
          onSignedOut()
          return
        }

        setProblem(error instanceof Refused ? error.message : 'Something went wrong.')
      }
    },
    [onSignedOut],
  )

  useEffect(() => {
    void read(api.read)
  }, [read])

  // A run over a library is minutes of reading somebody's NAS and outlives the
  // request that started it, so the screen keeps asking while one is under way
  // and stops the moment it is not.
  const working = state?.running === true
  const readRef = useRef(read)
  readRef.current = read

  useEffect(() => {
    if (!working) {
      return
    }

    const timer = setInterval(() => void readRef.current(api.read), WhileWorking)

    return () => clearInterval(timer)
  }, [working])

  const ask = async () => {
    setBusy(true)
    await read(api.start)
    setBusy(false)
  }

  if (state === null) {
    return <p className={problem === null ? 'hint' : 'problem'}>{problem ?? 'Asking the tool…'}</p>
  }

  const scenes = Number(state.scenes)
  const never = Number(state.neverChecked)

  return (
    <>
      <section className="card">
        <h2>Metadata</h2>

        {problem !== null && <p className="problem">{problem}</p>}
        {state.problem !== null && state.problem !== undefined && (
          <p className="problem">{state.problem}</p>
        )}

        <p className={scenes === 0 ? 'hint' : 'answer'}>
          {scenes === 0
            ? 'Nothing has been filed into this library yet, so there is nothing to check. This ' +
              'screen is for what happens afterwards: prdb corrects a title, a date or a cast ' +
              'entry, and the file written last spring still says the old thing.'
            : `${describe(scenes, 'scene')} in this library ${scenes === 1 ? 'was' : 'were'} filed by ` +
              `prdb-ordeno. ${standing(never, state.oldestCheckedAt)}`}
        </p>

        <p className="hint">
          {state.unattended
            ? `prdb-ordeno checks ${String(state.slice)} scenes every ${String(state.intervalHours)} ` +
              'hours on its own, least recently checked first, so the whole library comes round. ' +
              'The button is the same check over the whole library, now.'
            : 'prdb-ordeno checks nothing on its own. Pressing the button asks prdb about every ' +
              'scene it filed here, fifty at a time, and rewrites a metadata file it wrote itself ' +
              'when prdb no longer says what the file says. Settings → Library is where it can be ' +
              'told to do that on its own.'}
        </p>

        {/* The one thing worth saying twice, because it is what makes this safe
            to press: it writes over its own work and into empty names, and it
            moves nothing. */}
        <p className="hint">
          Nothing here moves or renames a file. A <code>movie.nfo</code> you wrote yourself is
          never touched, and an image is only ever written where there is none — which is also why
          a check cannot be undone, and does not need to be.
        </p>

        <div className="row buttons">
          <button
            type="button"
            onClick={() => void ask()}
            disabled={busy || state.running || scenes === 0}
            title="Asks prdb about every scene filed here and brings the metadata files it wrote up to date."
          >
            {state.running ? 'Checking…' : 'Check the library against prdb'}
          </button>
        </div>
      </section>

      {state.whatItDid !== null && state.whatItDid !== undefined && (
        <section className="card">
          <h2>What the last check did</h2>
          <p className="answer">{state.whatItDid}</p>

          {/* A run nobody started is a run that appeared out of nowhere on this
              screen, and it says where it came from — the same rule the Filing
              screen follows. */}
          {state.askedByTimer && (
            <p className="hint">
              Nobody asked for that check: the tool did it on its own, {String(state.slice)} scenes
              at a time.
            </p>
          )}

          {state.changed.length === 0 ? (
            <p className="hint">
              Nothing was written. Every scene it reached already said what prdb says.
            </p>
          ) : (
            <>
              {Number(state.changedTotal) > state.changed.length && (
                <p className="hint">
                  The first {String(state.changed.length)} of {String(state.changedTotal)}. The
                  sentence above counts all of them.
                </p>
              )}

              <RowTable heads={['Scene', 'What changed', 'What prdb calls it']}>
                {state.changed.map((scene) => (
                  <Changed key={scene.directory} scene={scene} />
                ))}
              </RowTable>
            </>
          )}
        </section>
      )}
    </>
  )
}

function describe(count: number, thing: string) {
  return `${count.toLocaleString('en')} ${thing}${count === 1 ? '' : 's'}`
}

/** Where the library stands, in the one sentence somebody actually wants. */
function standing(never: number, oldest: string | null | undefined) {
  if (never > 0) {
    return `${describe(never, 'scene')} ${never === 1 ? 'has' : 'have'} never been checked against ` +
      'what prdb says now.'
  }

  if (oldest === null || oldest === undefined) {
    return 'None of them has been checked yet.'
  }

  return `All of them have been checked since ${new Date(oldest).toLocaleDateString()}.`
}

function Changed({ scene }: { scene: RefreshedSceneState }) {
  const detail = (
    <>
      <p className="hint">
        <code>{scene.directory}</code>
      </p>

      {scene.sidecarMessage !== null && scene.sidecarMessage !== undefined && (
        <p className="hint">{scene.sidecarMessage}</p>
      )}

      {scene.artworkMessage !== null && scene.artworkMessage !== undefined && (
        <p className="hint">{scene.artworkMessage}</p>
      )}

      {scene.problem !== null && scene.problem !== undefined && (
        <p className="problem">{scene.problem}</p>
      )}
    </>
  )

  return (
    <Row name={<code>{scene.scene}</code>} detail={detail}>
      <td>
        {scene.sidecar && <span className="chip ready">metadata rewritten</span>}
        {scene.artwork && <span className="chip ready">image written</span>}
        {!scene.sidecar && !scene.artwork && <span className="chip">left as it was</span>}
      </td>
      <td>{scene.title ?? <span className="hint">—</span>}</td>
    </Row>
  )
}
