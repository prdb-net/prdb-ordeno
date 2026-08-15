import { useCallback, useEffect, useState } from 'react'

import {
  Refused,
  SignedOut,
  review as api,
  type ReviewCandidateState,
  type ReviewDecisionState,
  type ReviewEntryState,
  type ReviewFilter,
  type ReviewQueueState,
  type VideoSearchState,
} from '../api/client'

/**
 * The last rung of the ladder, which is a person. Everything prdb could not
 * settle is here, and the measure of this screen is how few keystrokes the
 * common case takes: three candidates are three buttons, and a search starts
 * from the file's own name rather than an empty box.
 *
 * Nothing here moves a file. Resolving says what a video *is*; filing is still
 * the run somebody asks for on the Downloads screen, and the screen says so
 * where the resolving happens.
 */
export default function ReviewScreen({ onSignedOut }: { onSignedOut: () => void }) {
  const [queue, setQueue] = useState<ReviewQueueState | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [filter, setFilter] = useState<ReviewFilter>('waiting')
  const [site, setSite] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [chosen, setChosen] = useState<number[]>([])
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
    const answer = await call(() =>
      // "no site" is its own group rather than the absence of one, so it is a
      // flag rather than an empty site id.
      api.read(filter, site === 'none' ? undefined : (site ?? undefined), site === 'none', page),
    )

    if (answer !== null) {
      setQueue(answer)
      setChosen([])
    }
  }, [call, filter, site, page])

  useEffect(() => {
    void read()
  }, [read])

  if (queue === null) {
    return <p className={problem === null ? 'hint' : 'problem'}>{problem ?? 'Asking the tool…'}</p>
  }

  const summary = queue.summary

  /**
   * A decision came back. The row is dropped from the list here rather than by
   * reading the page again: somebody settling forty files should not wait for
   * forty pages, and the counts come with the answer.
   */
  const settled = async (fileId: number, decide: () => Promise<ReviewDecisionState>) => {
    setBusy(true)

    const decision = await call(decide)

    if (decision !== null) {
      setQueue((current) =>
        current === null
          ? current
          : {
              ...current,
              entries: current.entries.filter((entry) => Number(entry.fileId) !== fileId),
              total: Number(current.total) - 1,
              summary: decision.summary,
            },
      )

      setChosen((current) => current.filter((id) => id !== fileId))
    }

    setBusy(false)
  }

  const dismissChosen = async () => {
    setBusy(true)

    const decision = await call(() => api.dismissMany(chosen))

    if (decision !== null) {
      await read()

      if (decision.problem !== null && decision.problem !== undefined) {
        setProblem(decision.problem)
      }
    }

    setBusy(false)
  }

  const move = (to: ReviewFilter) => {
    setFilter(to)
    setPage(1)
    setSite(null)
  }

  const pages = Number(queue.pages)

  return (
    <>
      <p className="summary">{summary.whatIsWaiting}</p>

      {problem !== null && <p className="problem">{problem}</p>}
      {queue.problem !== null && queue.problem !== undefined && (
        <p className="problem">{queue.problem}</p>
      )}

      <nav className="views">
        <Tab now={filter} to="waiting" count={summary.waiting} onChosen={move}>
          Waiting
        </Tab>
        <Tab now={filter} to="assigned" count={summary.assigned} onChosen={move}>
          Settled by you
        </Tab>
        <Tab now={filter} to="dismissed" count={summary.dismissed} onChosen={move}>
          Not to be filed
        </Tab>
      </nav>

      {filter === 'waiting' && queue.sites.length > 1 && (
        <div className="sites">
          <button
            type="button"
            className={site === null ? 'chip chosen' : 'chip'}
            onClick={() => {
              setSite(null)
              setPage(1)
            }}
          >
            Everything
          </button>

          {queue.sites.map((known) => {
            const id = known.siteId ?? 'none'

            return (
              <button
                key={id}
                type="button"
                className={site === id ? 'chip chosen' : 'chip'}
                onClick={() => {
                  setSite(id)
                  setPage(1)
                }}
              >
                {known.name} ({String(known.waiting)})
              </button>
            )
          })}
        </div>
      )}

      {filter === 'waiting' && (
        <p className="hint">
          Settling a file here does not move it. It says what the video is; filing happens on the
          Downloads screen, when you ask for it.
        </p>
      )}

      {queue.entries.length === 0 ? (
        <p className="hint">Nothing in this list.</p>
      ) : (
        <>
          {filter === 'waiting' && (
            <div className="row">
              <button
                type="button"
                className="quiet"
                onClick={() =>
                  setChosen(
                    chosen.length === queue.entries.length
                      ? []
                      : queue.entries.map((entry) => Number(entry.fileId)),
                  )
                }
              >
                {chosen.length === queue.entries.length ? 'Select none' : 'Select all on this page'}
              </button>

              <button type="button" disabled={busy || chosen.length === 0} onClick={() => void dismissChosen()}>
                {chosen.length === 1
                  ? 'Leave this one alone'
                  : `Leave these ${String(chosen.length)} alone`}
              </button>
            </div>
          )}

          <ul className="queue">
            {queue.entries.map((entry) => (
              <Row
                key={String(entry.fileId)}
                entry={entry}
                filter={filter}
                busy={busy}
                chosen={chosen.includes(Number(entry.fileId))}
                onChosen={(fileId, picked) =>
                  setChosen((current) =>
                    picked ? [...current, fileId] : current.filter((id) => id !== fileId),
                  )
                }
                onDecided={settled}
                onSearch={(query, within) => call(() => api.search(query, within))}
              />
            ))}
          </ul>

          {pages > 1 && (
            <div className="pager">
              <button type="button" disabled={busy || page === 1} onClick={() => setPage(page - 1)}>
                Previous
              </button>

              <span className="hint">
                Page {String(queue.page)} of {String(pages)}, {String(queue.total)} in this list
              </span>

              <button type="button" disabled={busy || page >= pages} onClick={() => setPage(page + 1)}>
                Next
              </button>
            </div>
          )}
        </>
      )}
    </>
  )
}

function Tab({
  now,
  to,
  count,
  onChosen,
  children,
}: {
  now: ReviewFilter
  to: ReviewFilter
  count: number | string
  onChosen: (to: ReviewFilter) => void
  children: string
}) {
  return (
    <button
      type="button"
      className={now === to ? 'view chosen' : 'view'}
      onClick={() => onChosen(to)}
    >
      {children} ({String(count)})
    </button>
  )
}

/**
 * One file, with the evidence the tool has about it. A row that prdb could name
 * the site of is further along than one it knows nothing about, and it must not
 * be shown as the same kind of thing — which is what the answer line does.
 */
function Row({
  entry,
  filter,
  busy,
  chosen,
  onChosen,
  onDecided,
  onSearch,
}: {
  entry: ReviewEntryState
  filter: ReviewFilter
  busy: boolean
  chosen: boolean
  onChosen: (fileId: number, picked: boolean) => void
  onDecided: (fileId: number, decide: () => Promise<ReviewDecisionState>) => Promise<void>
  onSearch: (query: string, site?: string) => Promise<VideoSearchState | null>
}) {
  const fileId = Number(entry.fileId)
  const [searching, setSearching] = useState(false)
  const [query, setQuery] = useState(() => searchable(entry.name))
  const [found, setFound] = useState<VideoSearchState | null>(null)
  const [asking, setAsking] = useState(false)

  const search = async () => {
    setAsking(true)
    setFound(await onSearch(query))
    setAsking(false)
  }

  return (
    <li>
      <div className="row">
        <label className="pick">
          {filter === 'waiting' && (
            <input
              type="checkbox"
              checked={chosen}
              onChange={(event) => onChosen(fileId, event.target.checked)}
            />
          )}
          <code>{entry.name}</code>
        </label>

        <span className="hint">{size(Number(entry.sizeBytes))}</span>
      </div>

      {entry.recognised !== null && entry.recognised !== undefined && (
        <p className={entry.recognised.state === 'unrecognised' ? 'hint' : 'answer'}>
          {entry.recognised.answer}
          {entry.recognised.because !== null && entry.recognised.because !== undefined && (
            <span className="hint"> — {entry.recognised.because}</span>
          )}
        </p>
      )}

      {entry.decision !== null && entry.decision !== undefined ? (
        <div className="row">
          <p className="answer">
            {entry.decision.answer}
            <span className="hint">
              {' '}
              — {new Date(entry.decision.decidedAt).toLocaleString()}
            </span>
          </p>

          <button
            type="button"
            className="quiet"
            disabled={busy}
            onClick={() => void onDecided(fileId, () => api.forget(fileId))}
          >
            Undo
          </button>
        </div>
      ) : (
        <>
          {entry.candidates.length > 0 && (
            <ul className="candidates">
              {entry.candidates.map((candidate) => (
                <Candidate
                  key={candidate.videoId}
                  candidate={candidate}
                  busy={busy}
                  onAccepted={() =>
                    void onDecided(fileId, () => api.assign(fileId, candidate.videoId))
                  }
                />
              ))}
            </ul>
          )}

          <div className="row">
            <button
              type="button"
              className="quiet"
              onClick={() => setSearching(!searching)}
              disabled={busy}
            >
              {searching ? 'Close the search' : 'Find it in prdb'}
            </button>

            <button
              type="button"
              className="quiet"
              disabled={busy}
              onClick={() => void onDecided(fileId, () => api.dismiss(fileId))}
              title="The tool leaves this file alone. It is not deleted and it stays in the list of what was found."
            >
              Not to be filed
            </button>
          </div>

          {searching && (
            <div className="search">
              {/* Pre-filled from the file's own name, because that is what the
                  person would have typed. The point of this screen is how few
                  keystrokes the common case takes. */}
              <input
                type="search"
                value={query}
                aria-label={`Search prdb for ${entry.name}`}
                onChange={(event) => setQuery(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    void search()
                  }
                }}
              />

              <button type="button" onClick={() => void search()} disabled={asking || busy}>
                {asking ? 'Asking prdb…' : 'Search'}
              </button>

              {/* prdb refusing and prdb having nothing are different answers,
                  and showing the first as the second would send somebody
                  looking for a video that is there. */}
              {found !== null && !found.answered && (
                <p className="problem">{found.problem ?? 'prdb could not be asked.'}</p>
              )}

              {found !== null &&
                found.answered &&
                (found.videos.length === 0 ? (
                  <p className="hint">Nothing under that. Try fewer words.</p>
                ) : (
                  <ul className="candidates">
                    {found.videos.map((video) => (
                      <li key={video.videoId}>
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() =>
                            void onDecided(fileId, () => api.assign(fileId, video.videoId))
                          }
                        >
                          This one
                        </button>
                        <span className="answer">{video.answer}</span>
                      </li>
                    ))}
                  </ul>
                ))}
            </div>
          )}
        </>
      )}
    </li>
  )
}

function Candidate({
  candidate,
  busy,
  onAccepted,
}: {
  candidate: ReviewCandidateState
  busy: boolean
  onAccepted: () => void
}) {
  return (
    <li>
      <button type="button" onClick={onAccepted} disabled={busy}>
        This one
      </button>
      <span className="answer">{candidate.answer}</span>
    </li>
  )
}

/**
 * A file name as something to search prdb for: no extension, no separators, and
 * without the words that describe the file rather than the scene. It is a guess
 * at what somebody would have typed, and it is theirs to edit.
 */
function searchable(name: string): string {
  const noise =
    /\b(1080p|2160p|720p|480p|4k|uhd|hdr|x264|x265|h264|h265|hevc|xvid|mp4|mkv|wmv|avi|web|webrip|dl|rip|sd|hd)\b/gi

  return name
    .replace(/\.[^.]+$/, '')
    .replace(/^.*[/\\]/, '')
    .replace(/[._-]+/g, ' ')
    .replace(noise, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

/** In the units a NAS shows, which are powers of two whatever the disk was sold as. */
function size(bytes: number): string {
  const units = ['bytes', 'KiB', 'MiB', 'GiB', 'TiB']
  let value = bytes
  let unit = 0

  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit += 1
  }

  return `${unit === 0 ? String(value) : value.toFixed(1)} ${units[unit] ?? ''}`
}
