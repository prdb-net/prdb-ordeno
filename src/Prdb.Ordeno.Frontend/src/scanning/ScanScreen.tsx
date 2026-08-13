import { useCallback, useEffect, useRef, useState } from 'react'

import {
  Refused,
  SignedOut,
  scanning as api,
  type RecognisedState,
  type ScanState,
  type ScannedFileState,
} from '../api/client'

/** How often the screen asks again while something is under way. */
const WhileWorking = 2000

/**
 * What is in the download directories, and what the tool has made of it. The
 * first screen someone opens once the setup is done, because the question they
 * came with is "is it dealing with my downloads" — and in this version the
 * honest answer is "it has found them and knows what they are".
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
      <p className="summary">{state.whatItFound}</p>

      {problem !== null && <p className="problem">{problem}</p>}
      {state.problem !== null && state.problem !== undefined && (
        <p className="problem">{state.problem}</p>
      )}

      <section className="card">
        <h2>Download directories</h2>

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

        <p className="hint">
          {state.lastScanFinishedAt === null || state.lastScanFinishedAt === undefined
            ? 'Not scanned since the tool started. It looks by itself every few minutes.'
            : `Last looked at ${new Date(state.lastScanFinishedAt).toLocaleString()}. The tool looks by itself every few minutes.`}
        </p>

        <button type="button" onClick={() => void ask(api.now)} disabled={busy || state.scanning}>
          {state.scanning ? 'Looking…' : 'Scan now'}
        </button>
      </section>

      <section className="card">
        <h2>What prdb says they are</h2>

        {identification.whatItRecognised === null ||
        identification.whatItRecognised === undefined ? (
          <p className="hint">
            Nothing has been asked about yet. The tool asks prdb once a file has finished
            downloading.
          </p>
        ) : (
          <p>{identification.whatItRecognised}</p>
        )}

        {identification.problem !== null && identification.problem !== undefined && (
          <p className="problem">{identification.problem}</p>
        )}

        {identification.notBefore !== null && identification.notBefore !== undefined && (
          <p className="hint">
            Asking again after {new Date(identification.notBefore).toLocaleString()}.
          </p>
        )}

        {/* VISION.md says this plainly and so does onboarding; the screen that
            shows the answers is the other place it belongs. */}
        <p className="hint">
          The name, size and hashes of every file examined are sent to prdb. That is what
          identifying them is.
        </p>

        {Number(identification.perceptualBacklog) > 0 && (
          <p className="hint">
            {String(identification.perceptualBacklog)} waiting for a perceptual hash, one at a time
            in the background. Nothing is held up by it — prdb still compares these hashes exactly,
            so today they find no more than the plain file hash does.
          </p>
        )}

        <button
          type="button"
          onClick={() => void ask(api.identify)}
          disabled={busy || identification.running}
        >
          {identification.running ? 'Asking prdb…' : 'Identify now'}
        </button>
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

          <ul className="files">
            {state.files.map((file) => (
              <Found key={String(file.id)} file={file} />
            ))}
          </ul>
        </section>
      )}
    </>
  )
}

function Found({ file }: { file: ScannedFileState }) {
  return (
    <li>
      <div className="row">
        <code>{file.name}</code>
        <span className={file.ready ? 'chip ready' : 'chip'} title={
          file.ready
            ? 'Seen unchanged by two scans, so it has finished downloading.'
            : 'Seen once, or changed since the last look. The tool waits for it to stop changing.'
        }>
          {file.ready ? 'ready' : 'waiting'}
        </span>
      </div>
      <p className="hint">{size(Number(file.sizeBytes))}</p>
      <Recognised recognised={file.recognised} ready={file.ready} />
    </li>
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
      <p className="hint">
        {ready ? 'Waiting to be identified.' : 'Not identified yet — it is still arriving.'}
      </p>
    )
  }

  return (
    <p className={recognised.state === 'unrecognised' ? 'hint' : 'answer'}>
      {recognised.answer}
      {recognised.because !== null && recognised.because !== undefined && (
        <span className="hint"> — {recognised.because}</span>
      )}
    </p>
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
