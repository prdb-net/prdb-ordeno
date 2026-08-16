import { useCallback, useEffect, useRef, useState } from 'react'

import {
  Refused,
  SignedOut,
  filing as api,
  type FiledFileState,
  type FilingState,
  type PlannedFileState,
} from '../api/client'
import RowTable, { Row } from '../ui/RowTable'

/** How often the screen asks again while a run is under way. */
const WhileWorking = 2000

/**
 * What would happen to the videos that have been recognised, and the button
 * that makes it happen.
 *
 * ADR 0022: nothing here moves a file until somebody has read the plan and
 * asked for it. The two buttons are deliberately not one — the first is safe to
 * press and the second is not, and a screen that hides that behind a single
 * "go" would be hiding the only part of this the user has to think about.
 *
 * It is an area of its own rather than a card under the downloads, because it
 * is the one place in the tool that moves a file: it needs the room to say what
 * it would do to each of them, and what it did to each of them afterwards.
 */
export default function FilingScreen({ onSignedOut }: { onSignedOut: () => void }) {
  const [state, setState] = useState<FilingState | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const read = useCallback(
    async (call: () => Promise<FilingState>) => {
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

  // Both runs outlive the request that started them, so the screen keeps asking
  // while one is under way and stops the moment it is not. A tool left alone
  // for weeks should not talk to itself every two seconds.
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

  const ask = async (call: () => Promise<FilingState>) => {
    setBusy(true)
    await read(call)
    setBusy(false)
  }

  if (state === null) {
    return <p className={problem === null ? 'hint' : 'problem'}>{problem ?? 'Asking the tool…'}</p>
  }

  const planned = state.plannedAt !== null && state.plannedAt !== undefined
  const wouldFile = Number(state.wouldFile)

  return (
    <>
      <section className="card">
        <h2>Filing</h2>

        {problem !== null && <p className="problem">{problem}</p>}
        {state.problem !== null && state.problem !== undefined && (
          <p className="problem">{state.problem}</p>
        )}

        <p className={planned ? 'answer' : 'hint'}>
          {state.whatItWouldDo ??
            'Nothing has been worked out yet. The tool can tell you where each recognised video ' +
              'would go before it moves anything.'}
        </p>

        <p className="hint">
          prdb-ordeno files nothing on its own. It moves a file when you press the second button
          below, and until there is a way to undo a run that went wrong it will not do it unasked.
        </p>

        <div className="row buttons">
          <button type="button" onClick={() => void ask(api.plan)} disabled={busy || state.running}>
            {state.running && !state.filing ? 'Working it out…' : 'Work out what would happen'}
          </button>

          <button
            type="button"
            onClick={() => void ask(api.file)}
            disabled={busy || state.running || !planned || wouldFile === 0}
            title={
              planned
                ? 'Moves the videos listed below into the library.'
                : 'Work out what would happen first.'
            }
          >
            {state.running && state.filing
              ? 'Filing…'
              : wouldFile === 1
                ? 'File this video'
                : `File these ${String(wouldFile)} videos`}
          </button>
        </div>
      </section>

      {state.plan.length > 0 && (
        <section className="card">
          <h2>What would happen</h2>

          {Number(state.planTotal) > state.plan.length && (
            <p className="hint">
              The first {String(state.plan.length)} of {String(state.planTotal)}. The sentence above
              counts all of them, and the button files all of them.
            </p>
          )}

          <RowTable heads={['File', 'What would happen', 'The scene']}>
            {state.plan.map((plan) => (
              <Planned key={String(plan.fileId)} plan={plan} />
            ))}
          </RowTable>
        </section>
      )}

      {state.whatItDid !== null && state.whatItDid !== undefined && (
        <section className="card">
          <h2>What the last run did</h2>
          <p className="answer">{state.whatItDid}</p>

          <RowTable heads={['File', 'What happened', 'Where it went']}>
            {state.results.map((result) => (
              <Filed key={String(result.fileId)} result={result} />
            ))}
          </RowTable>
        </section>
      )}
    </>
  )
}

/** What each outcome is called on a row, and what the word means. */
const outcomes: Record<string, { label: string; title: string }> = {
  filed: { label: 'would be filed', title: 'Into a scene directory that does not exist yet.' },
  collisionBroken: {
    label: 'name is taken',
    title:
      'Another scene is already filed under the name the layout wanted, so this one goes into a ' +
      'directory carrying its prdb scene id. Two scenes in one directory would become one entry.',
  },
  secondQuality: {
    label: 'second quality',
    title: 'The library holds this scene at another quality. Both are kept, in one directory.',
  },
  alreadyFiled: {
    label: 'already in the library',
    title: 'The same scene at the same quality. It is not filed, and it is not deleted either.',
  },
  blocked: { label: 'cannot be filed', title: 'The reason is on the row.' },
}

function Planned({ plan }: { plan: PlannedFileState }) {
  const outcome = outcomes[plan.outcome] ?? outcomes.blocked

  // Everything a single file has to say for itself: where it lands, what that
  // move costs, what happens to a file already there, and why it is blocked if
  // it is. On the line it would be four sentences competing with the next
  // file's four.
  const detail = (
    <>
      {plan.targetName !== null && plan.targetName !== undefined && (
        <p className="hint">
          → <code>{plan.directory}</code>/<code>{plan.targetName}</code>
          {plan.movement === 'copyThenDelete' &&
            ' — copied, checked and only then deleted from the download directory, because the two ' +
              'are on different filesystems.'}
        </p>
      )}

      {plan.relabelTo !== null && plan.relabelTo !== undefined && (
        <p className="hint">
          <code>{plan.relabelFrom}</code> is renamed to <code>{plan.relabelTo}</code> first, so both
          versions are listed by their quality.
        </p>
      )}

      {/* The second thing a filing writes. It is the metadata the media server
          actually shows, and it can land next to a file somebody wrote
          themselves — so it says what it would do before it does it. */}
      {plan.sidecar !== null && plan.sidecar !== undefined && <p className="hint">{plan.sidecar}</p>}

      {plan.message !== null && plan.message !== undefined && <p className="hint">{plan.message}</p>}
    </>
  )

  const says = [plan.targetName, plan.relabelTo, plan.sidecar, plan.message].some(
    (part) => part !== null && part !== undefined,
  )

  return (
    <Row name={<code>{plan.name}</code>} detail={says ? detail : undefined}>
      <td>
        <span className={plan.moves ? 'chip ready' : 'chip'} title={outcome?.title}>
          {outcome?.label}
        </span>
      </td>
      <td>{plan.scene ?? <span className="hint">—</span>}</td>
    </Row>
  )
}

function Filed({ result }: { result: FiledFileState }) {
  const detail =
    (result.message !== null && result.message !== undefined) ||
    (result.sidecar !== null && result.sidecar !== undefined) ? (
      <>
        {result.message !== null && result.message !== undefined && (
          <p className={result.state === 'failed' ? 'problem' : 'hint'}>{result.message}</p>
        )}

        {/* Only ever present when something is worth saying: the sidecar was
            left alone, or it could not be written. A video that got one says so
            by this being absent. */}
        {result.sidecar !== null && result.sidecar !== undefined && (
          <p className="hint">{result.sidecar}</p>
        )}
      </>
    ) : undefined

  return (
    <Row name={<code>{result.name}</code>} detail={detail}>
      <td>
        <span className={result.state === 'filed' ? 'chip ready' : 'chip'}>{result.state}</span>
      </td>
      <td>
        {result.targetName !== null && result.targetName !== undefined && result.state === 'filed' ? (
          <code>{result.targetName}</code>
        ) : (
          <span className="hint">—</span>
        )}
      </td>
    </Row>
  )
}
