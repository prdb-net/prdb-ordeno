import { useCallback, useEffect, useRef, useState } from 'react'

import {
  Refused,
  SignedOut,
  filing as api,
  type FiledFileState,
  type FilingState,
  type PlannedFileState,
} from '../api/client'

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
 */
export default function FilingCard({ onSignedOut }: { onSignedOut: () => void }) {
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
    return (
      <section className="card">
        <h2>Filing</h2>
        <p className={problem === null ? 'hint' : 'problem'}>{problem ?? 'Asking the tool…'}</p>
      </section>
    )
  }

  const planned = state.plannedAt !== null && state.plannedAt !== undefined
  const wouldFile = Number(state.wouldFile)

  return (
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

      <div className="row">
        <button type="button" onClick={() => void ask(api.plan)} disabled={busy || state.running}>
          {state.running && !state.filing ? 'Working it out…' : 'Work out what would happen'}
        </button>

        <button
          type="button"
          onClick={() => void ask(api.file)}
          disabled={busy || state.running || !planned || wouldFile === 0}
          title={
            planned
              ? 'Moves the videos listed above into the library.'
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

      {state.plan.length > 0 && (
        <>
          {Number(state.planTotal) > state.plan.length && (
            <p className="hint">
              The first {String(state.plan.length)} of {String(state.planTotal)}. The sentence above
              counts all of them, and the button files all of them.
            </p>
          )}

          <ul className="files">
            {state.plan.map((plan) => (
              <Planned key={String(plan.fileId)} plan={plan} />
            ))}
          </ul>
        </>
      )}

      {state.whatItDid !== null && state.whatItDid !== undefined && (
        <>
          <h3>What the last run did</h3>
          <p className="answer">{state.whatItDid}</p>

          <ul className="files">
            {state.results.map((result) => (
              <Filed key={String(result.fileId)} result={result} />
            ))}
          </ul>
        </>
      )}
    </section>
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

  return (
    <li>
      <div className="row">
        <code>{plan.name}</code>
        <span className={plan.moves ? 'chip ready' : 'chip'} title={outcome?.title}>
          {outcome?.label}
        </span>
      </div>

      {plan.scene !== null && plan.scene !== undefined && <p className="answer">{plan.scene}</p>}

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

      {plan.message !== null && plan.message !== undefined && <p className="hint">{plan.message}</p>}
    </li>
  )
}

function Filed({ result }: { result: FiledFileState }) {
  return (
    <li>
      <div className="row">
        <code>{result.name}</code>
        <span className={result.state === 'filed' ? 'chip ready' : 'chip'}>{result.state}</span>
      </div>

      {result.targetName !== null && result.targetName !== undefined && result.state === 'filed' && (
        <p className="hint">
          → <code>{result.targetName}</code>
        </p>
      )}

      {result.message !== null && result.message !== undefined && (
        <p className={result.state === 'failed' ? 'problem' : 'hint'}>{result.message}</p>
      )}
    </li>
  )
}
