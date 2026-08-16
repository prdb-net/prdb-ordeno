import { useCallback, useEffect, useRef, useState } from 'react'

import {
  Refused,
  SignedOut,
  scanning as api,
  type RecognisedState,
  type ScanState,
  type ScannedFileState,
} from '../api/client'
import RowTable, { Row } from '../ui/RowTable'

/** How often the screen asks again while something is under way. */
const WhileWorking = 2000

/**
 * The last run, in one line — including the two cases that are otherwise
 * invisible: a run that has not happened yet, and a run that found nothing
 * ready. The second is what a file being asked about looks like during the
 * minute it takes to count as finished, and it is the answer to "I pressed the
 * button and nothing happened".
 */
function lastRun(identification: ScanState['identification']): string {
  if (identification.running) {
    return 'Asking prdb now.'
  }

  if (identification.lastRunFinishedAt === null || identification.lastRunFinishedAt === undefined) {
    return 'Not asked since the tool started. It asks by itself every minute.'
  }

  const when = new Date(identification.lastRunFinishedAt).toLocaleString()

  return Number(identification.lastRunAsked) === 0
    ? `Last run ${when}: nothing was ready to ask about. A file counts as ready a minute after it stops changing.`
    : `Last asked ${when}: ${String(identification.lastRunAsked)} files.`
}

/**
 * What is in the download directories, and what the tool has made of it. The
 * first area someone opens once the setup is done, because the question they
 * came with is "is it dealing with my downloads".
 *
 * Two things and no more: what the tool is doing about them, and the files
 * themselves. What would *happen* to the files is the filing area — this screen
 * answers "what is there", and one screen that also answered "and what shall we
 * do with it" was the one people had to scroll past to reach their own files.
 */
export default function ScanScreen({ onSignedOut }: { onSignedOut: () => void }) {
  const [state, setState] = useState<ScanState | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const read = useCallback(
    async (call: () => Promise<ScanState>) => {
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

  // While a scan or an identification run is under way the screen keeps asking,
  // because both outlive the request that started them. Once they are finished,
  // nothing polls: a tool that is left alone for weeks should not talk to itself
  // every two seconds.
  const working = state?.scanning === true || state?.identification.running === true
  const readRef = useRef(read)
  readRef.current = read

  useEffect(() => {
    if (!working) {
      return
    }

    const timer = setInterval(() => void readRef.current(api.read), WhileWorking)

    return () => clearInterval(timer)
  }, [working])

  const ask = async (call: () => Promise<ScanState>) => {
    setBusy(true)
    await read(call)
    setBusy(false)
  }

  if (state === null) {
    return <p className={problem === null ? 'hint' : 'problem'}>{problem ?? 'Asking the tool…'}</p>
  }

  const identification = state.identification

  return (
    <>
      <section className="card">
        <h2>Downloads</h2>

        {problem !== null && <p className="problem">{problem}</p>}

        {/* The answer to "is it dealing with my downloads", before anything that
            explains how. */}
        <p className={identification.whatItRecognised === null ? 'hint' : 'answer'}>
          {identification.whatItRecognised ??
            'Nothing has been asked about yet. The tool asks prdb once a file has finished downloading.'}
        </p>

        {identification.problem !== null && identification.problem !== undefined && (
          <p className="problem">{identification.problem}</p>
        )}

        <ul className="directories">
          {state.sources.map((source) => (
            <li key={String(source.sourceId)}>
              <div className="row">
                <code>{source.path}</code>
                {source.reachable && (
                  <span className="hint">
                    {Number(source.total) === 0
                      ? 'nothing found'
                      : `${String(source.ready)} ready, ${String(source.settling)} waiting`}
                  </span>
                )}
              </div>
              {!source.reachable && <p className="problem">{source.problem}</p>}
            </li>
          ))}
        </ul>

        <div className="row buttons">
          <button type="button" onClick={() => void ask(api.now)} disabled={busy || state.scanning}>
            {state.scanning ? 'Looking…' : 'Scan now'}
          </button>

          <button
            type="button"
            onClick={() => void ask(api.identify)}
            disabled={busy || identification.running}
          >
            {identification.running ? 'Asking prdb…' : 'Identify now'}
          </button>
        </div>

        <p className="hint">
          {state.lastScanFinishedAt === null || state.lastScanFinishedAt === undefined
            ? 'Not scanned since the tool started. It looks by itself every few minutes.'
            : `Last looked at ${new Date(state.lastScanFinishedAt).toLocaleString()}. The tool looks by itself every few minutes.`}
        </p>

        {/* What the last run did, including when it did nothing. Without this a
            run that found nothing ready and a run that never happened look the
            same from here — which is what pressing the button during the minute
            a file takes to settle looks like, and it reads as a broken button. */}
        <p className="hint">{lastRun(identification)}</p>

        {identification.notBefore !== null && identification.notBefore !== undefined && (
          <p className="hint">
            Asking again after {new Date(identification.notBefore).toLocaleString()}.
          </p>
        )}

        {Number(identification.perceptualBacklog) > 0 && (
          <p className="hint">
            {String(identification.perceptualBacklog)} waiting for a perceptual hash, one at a time
            in the background. Nothing is held up by it — prdb still compares these hashes exactly,
            so today they find no more than the plain file hash does.
          </p>
        )}

        {/* VISION.md says this plainly and so does onboarding; the screen that
            shows the answers is the other place it belongs. */}
        <p className="hint">
          The name, size and hashes of every file examined are sent to prdb. That is what
          identifying them is.
        </p>
      </section>

      {state.files.length > 0 && (
        <section className="card">
          <h2>What is there</h2>

          {/* The API types every integer as a number or a string, so anything
              arithmetic goes through Number first. */}
          {Number(state.total) > state.files.length && (
            <p className="hint">
              The {String(state.files.length)} most recently found, of {String(state.total)}.
            </p>
          )}

          <RowTable heads={['File', 'Size', 'State', 'What prdb says']}>
            {state.files.map((file) => (
              <Found key={String(file.id)} file={file} />
            ))}
          </RowTable>
        </section>
      )}
    </>
  )
}

function Found({ file }: { file: ScannedFileState }) {
  return (
    <Row
      name={<code>{file.name}</code>}
      detail={
        <>
          <p className="hint">
            <code>{file.path}</code>
          </p>
          <p className="hint">Found {new Date(file.firstSeenAt).toLocaleString()}.</p>
          {file.recognised !== null && file.recognised !== undefined && (
            <p className="hint">
              Asked {new Date(file.recognised.askedAt).toLocaleString()}
              {file.recognised.because !== null &&
                file.recognised.because !== undefined &&
                ` — ${file.recognised.because}`}
              {Number(file.recognised.candidates) > 0 &&
                `, ${String(file.recognised.candidates)} candidates in the review queue`}
              .
            </p>
          )}
        </>
      }
    >
      <td>{size(Number(file.sizeBytes))}</td>
      <td>
        <span
          className={file.ready ? 'chip ready' : 'chip'}
          title={
            file.ready
              ? 'Seen unchanged by two scans, so it has finished downloading.'
              : 'Seen once, or changed since the last look. The tool waits for it to stop changing.'
          }
        >
          {file.ready ? 'ready' : 'waiting'}
        </span>
      </td>
      <td>
        <Recognised recognised={file.recognised} ready={file.ready} />
      </td>
    </Row>
  )
}

/**
 * What prdb answered about one file. The four answers are not four degrees of
 * the same thing: a known site is a result, and showing it as a failure would
 * misrepresent the file that is furthest along of the ones still here.
 */
function Recognised({
  recognised,
  ready,
}: {
  recognised: RecognisedState | null | undefined
  ready: boolean
}) {
  if (recognised === null || recognised === undefined) {
    return (
      <span className="hint">
        {ready ? 'waiting to be identified' : 'still arriving'}
      </span>
    )
  }

  return (
    <span className={recognised.state === 'unrecognised' ? 'hint' : 'answer'}>
      {recognised.answer}
    </span>
  )
}

/**
 * In the units a NAS shows, which are powers of two whatever the disk was sold
 * as. A file's size is the one number on this screen someone will compare with
 * what their file manager says.
 */
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
