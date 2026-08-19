import { useCallback, useEffect, useRef, useState } from 'react'

import {
  Refused,
  SignedOut,
  history as api,
  type HistoryState,
  type LoggedOperationState,
  type LoggedRunState,
  type UndoState,
} from '../api/client'
import RowTable, { Row } from '../ui/RowTable'

/** How often the screen asks again while a check or an undo is under way. */
const WhileWorking = 2000

/**
 * What the tool has done to somebody's files, and the way back out of it —
 * ADR 0028 and ADR 0029.
 *
 * Newest first, because the run somebody wants is almost always the last one.
 * Each run opens onto the files it moved, and each of those says where it came
 * from and why the tool believed that was the right place for it — which is the
 * half of this screen that has nothing to do with undo and everything to do
 * with answering "why is this file here".
 *
 * The two buttons are deliberately not one, exactly as filing's are: the first
 * is safe to press and says what would happen, and the second moves files.
 */
export default function HistoryScreen({ onSignedOut }: { onSignedOut: () => void }) {
  const [log, setLog] = useState<HistoryState | null>(null)
  const [undo, setUndo] = useState<UndoState | null>(null)
  const [page, setPage] = useState(1)
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const call = useCallback(
    async <T,>(what: () => Promise<T>): Promise<T | null> => {
      try {
        const answer = await what()
        setProblem(null)

        return answer
      } catch (error) {
        if (error instanceof SignedOut) {
          onSignedOut()

          return null
        }

        setProblem(error instanceof Refused ? error.message : 'Something went wrong.')

        return null
      }
    },
    [onSignedOut],
  )

  const read = useCallback(async () => {
    const answer = await call(() => api.read(page))

    if (answer !== null) {
      setLog(answer)
    }
  }, [call, page])

  useEffect(() => {
    void read()
  }, [read])

  useEffect(() => {
    void (async () => {
      const answer = await call(api.undoState)

      if (answer !== null) {
        setUndo(answer)
      }
    })()
  }, [call])

  // An undo outlives the request that started it, so the screen keeps asking
  // while one is under way and stops the moment it is not. When it stops, the
  // log is read again: every entry it put back is stamped, and a screen still
  // offering to undo what has just been undone is a screen lying about the
  // library.
  const working = undo?.running === true
  const refresh = useRef({ read, call })
  refresh.current = { read, call }

  useEffect(() => {
    if (!working) {
      void refresh.current.read()

      return
    }

    const timer = setInterval(() => {
      void (async () => {
        const answer = await refresh.current.call(api.undoState)

        if (answer !== null) {
          setUndo(answer)
        }
      })()
    }, WhileWorking)

    return () => clearInterval(timer)
  }, [working])

  const ask = async (what: () => Promise<UndoState>) => {
    setBusy(true)

    const answer = await call(what)

    if (answer !== null) {
      setUndo(answer)
    }

    setBusy(false)
  }

  if (log === null) {
    return <p className={problem === null ? 'hint' : 'problem'}>{problem ?? 'Asking the tool…'}</p>
  }

  const pages = Number(log.pages)

  return (
    <>
      <section className="card">
        <h2>History</h2>

        {problem !== null && <p className="problem">{problem}</p>}
        {undo?.problem !== null && undo?.problem !== undefined && (
          <p className="problem">{undo.problem}</p>
        )}

        <p className="hint">
          Every file the tool has moved is here, with the reason it moved it. A run can be checked
          before it is put back, and anything that cannot be put back safely is left exactly as it
          is and says why.
        </p>

        {undo?.whatItWouldDo !== null && undo?.whatItWouldDo !== undefined && (
          <p className="answer">{undo.whatItWouldDo}</p>
        )}

        {undo?.whatItDid !== null && undo?.whatItDid !== undefined && (
          <p className="answer">{undo.whatItDid}</p>
        )}

        {Number(log.total) === 0 && (
          <p className="hint">
            Nothing has been filed yet. The moment something is, this is where it says what it did
            and where it came from.
          </p>
        )}
      </section>

      {log.runs.length > 0 && (
        <section className="card">
          <h2>Runs</h2>

          <RowTable heads={['When', 'What it did', 'Files']}>
            {log.runs.map((run) => (
              <RunRow
                key={String(run.id)}
                run={run}
                undo={undo}
                busy={busy}
                onCheck={() => void ask(() => api.checkRun(Number(run.id)))}
                onUndo={() => void ask(() => api.undoRun(Number(run.id)))}
                onCheckOne={(id) => void ask(() => api.checkOperation(id))}
                onUndoOne={(id) => void ask(() => api.undoOperation(id))}
              />
            ))}
          </RowTable>

          {pages > 1 && (
            <div className="row buttons">
              <button type="button" onClick={() => setPage(page - 1)} disabled={page <= 1}>
                Newer
              </button>

              <span className="hint">
                Page {String(log.page)} of {String(log.pages)}
              </span>

              <button type="button" onClick={() => setPage(page + 1)} disabled={page >= pages}>
                Older
              </button>
            </div>
          )}
        </section>
      )}

      {undo !== null && undo.plan.length > 0 && (
        <section className="card">
          <h2>What putting it back would do</h2>

          <RowTable heads={['File', 'What would happen']}>
            {undo.plan.map((plan) => (
              <Row
                key={String(plan.operationId)}
                name={<code>{plan.name}</code>}
                detail={<p className="hint">{plan.message}</p>}
              >
                <td>
                  <span className={plan.outcome === 'returns' ? 'chip ready' : 'chip'}>
                    {plan.outcome === 'returns' ? 'goes back' : 'cannot go back'}
                  </span>
                </td>
              </Row>
            ))}
          </RowTable>
        </section>
      )}

      {undo !== null && undo.results.length > 0 && (
        <section className="card">
          <h2>What the last undo did</h2>

          <RowTable heads={['File', 'What happened']}>
            {undo.results.map((result) => (
              <Row
                key={String(result.operationId)}
                name={<code>{result.name}</code>}
                detail={
                  <>
                    {result.message !== null && result.message !== undefined && (
                      <p className={result.state === 'failed' ? 'problem' : 'hint'}>
                        {result.message}
                      </p>
                    )}

                    {/* What could not be taken away with the file. Named rather
                        than swallowed: the video is back either way, and
                        somebody who wants the directory gone needs to know what
                        is still in it. */}
                    {result.leftovers !== null && result.leftovers !== undefined && (
                      <p className="hint">{result.leftovers}</p>
                    )}
                  </>
                }
              >
                <td>
                  <span className={result.state === 'returned' ? 'chip ready' : 'chip'}>
                    {result.state}
                  </span>
                </td>
              </Row>
            ))}
          </RowTable>
        </section>
      )}
    </>
  )
}

function RunRow({
  run,
  undo,
  busy,
  onCheck,
  onUndo,
  onCheckOne,
  onUndoOne,
}: {
  run: LoggedRunState
  undo: UndoState | null
  busy: boolean
  onCheck: () => void
  onUndo: () => void
  onCheckOne: (operationId: number) => void
  onUndoOne: (operationId: number) => void
}) {
  const running = undo?.running === true
  const mine = undo?.runId !== null && undo?.runId !== undefined && Number(undo.runId) === Number(run.id)
  const checked = mine && undo?.checkedAt !== null && undo?.checkedAt !== undefined
  const undone = Number(run.undone) > 0

  const detail = (
    <>
      {run.problem !== null && run.problem !== undefined && <p className="problem">{run.problem}</p>}

      {run.finishedAt === null && (
        <p className="hint">
          This run has no end in the log, so the tool stopped while it was working. What it had
          already done is below.
        </p>
      )}

      {run.kind === 'filing' && (
        <div className="row buttons">
          <button type="button" onClick={onCheck} disabled={busy || running || !run.canBeUndone}>
            {running && !undo?.undoing && mine
              ? 'Working it out…'
              : 'Check what putting it back would do'}
          </button>

          <button
            type="button"
            onClick={onUndo}
            disabled={busy || running || !run.canBeUndone || !checked}
            title={
              checked
                ? 'Moves the files of this run back where they came from.'
                : 'Check what would happen first.'
            }
          >
            {running && undo?.undoing === true && mine
              ? 'Putting it back…'
              : 'Put this run back'}
          </button>
        </div>
      )}

      {run.kind === 'undo' && (
        <p className="hint">
          This was an undo. There is no way back out of one: filing again is the button on the
          Filing screen.
        </p>
      )}

      {run.entries.length > 0 && (
        <RowTable heads={['What happened', 'Why', '']}>
          {run.entries.map((entry) => (
            <EntryRow
              key={String(entry.id)}
              entry={entry}
              undo={undo}
              busy={busy}
              onCheck={() => onCheckOne(Number(entry.id))}
              onUndo={() => onUndoOne(Number(entry.id))}
            />
          ))}
        </RowTable>
      )}

      {Number(run.operations) > run.entries.length && (
        <p className="hint">
          The first {String(run.entries.length)} of {String(run.operations)}. Putting the run back
          puts all of them back.
        </p>
      )}
    </>
  )

  return (
    <Row name={new Date(run.startedAt).toLocaleString()} detail={detail}>
      <td>
        {run.account}
        {/* A run nobody asked for — ADR 0031. It is the first thing somebody
            reading this page in the morning wants to know about a row they do
            not remember causing. */}
        {run.askedByTimer && (
          <>
            {' '}
            <span className="chip" title="Nobody asked for this run: the tool filed on its own.">
              on its own
            </span>
          </>
        )}
        {undone && (
          <>
            {' '}
            <span className="chip">
              {Number(run.undone) === Number(run.operations)
                ? 'put back'
                : `${String(run.undone)} of ${String(run.operations)} put back`}
            </span>
          </>
        )}
      </td>
      <td>{String(run.operations)}</td>
    </Row>
  )
}

function EntryRow({
  entry,
  undo,
  busy,
  onCheck,
  onUndo,
}: {
  entry: LoggedOperationState
  undo: UndoState | null
  busy: boolean
  onCheck: () => void
  onUndo: () => void
}) {
  const running = undo?.running === true
  const checked =
    undo?.operationId !== null &&
    undo?.operationId !== undefined &&
    Number(undo.operationId) === Number(entry.id) &&
    undo.checkedAt !== null &&
    undo.checkedAt !== undefined
  const undone = entry.undoneAt !== null && entry.undoneAt !== undefined

  const detail = (
    <>
      <p className="hint">
        <code>{entry.from}</code> → <code>{entry.to}</code>
        {entry.movement === 'copyThenDelete' &&
          ' — copied, checked and only then deleted, because the two are on different filesystems.'}
      </p>

      {/* What went in next to the video. It is here because it is what an undo
          would take away with it — and only ever what this run itself wrote. */}
      {entry.sidecar !== null && entry.sidecar !== undefined && (
        <p className="hint">
          It wrote <code>{entry.sidecar}</code>.
        </p>
      )}

      {entry.artwork !== null && entry.artwork !== undefined && (
        <p className="hint">
          It downloaded <code>{entry.artwork}</code>.
        </p>
      )}

      {undone ? (
        <p className="hint">
          Put back {new Date(entry.undoneAt ?? '').toLocaleString()}.
        </p>
      ) : (
        <div className="row buttons">
          <button type="button" onClick={onCheck} disabled={busy || running}>
            Check this one
          </button>

          <button
            type="button"
            onClick={onUndo}
            disabled={busy || running || !checked}
            title={checked ? 'Moves this file back where it came from.' : 'Check it first.'}
          >
            Put this file back
          </button>
        </div>
      )}
    </>
  )

  return (
    <Row name={<code>{entry.name}</code>} detail={detail}>
      <td>{entry.what}</td>
      <td>{entry.why}</td>
      <td>{undone && <span className="chip">put back</span>}</td>
    </Row>
  )
}
