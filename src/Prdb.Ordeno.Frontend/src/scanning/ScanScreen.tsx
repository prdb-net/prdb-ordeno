import { useCallback, useEffect, useRef, useState } from 'react'

import {
  Refused,
  SignedOut,
  scanning as api,
  type ScanState,
  type ScannedFileState,
} from '../api/client'

/** How often the screen asks again while a scan is under way. */
const WhileScanning = 2000

/**
 * What is in the download directories. The first screen someone opens once the
 * setup is done, because the question they came with is "is it dealing with my
 * downloads" — and in this version the honest answer is "it has found them".
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

  // While a scan is running the screen keeps asking, because the scan outlives
  // the request that started it. Once it is finished, nothing polls: a tool
  // that is left alone for weeks should not talk to itself every two seconds.
  const scanning = state?.scanning === true
  const readRef = useRef(read)
  readRef.current = read

  useEffect(() => {
    if (!scanning) {
      return
    }

    const timer = setInterval(() => void readRef.current(api.read), WhileScanning)

    return () => clearInterval(timer)
  }, [scanning])

  const scanNow = async () => {
    setBusy(true)
    await read(api.now)
    setBusy(false)
  }

  if (state === null) {
    return <p className={problem === null ? 'hint' : 'problem'}>{problem ?? 'Asking the tool…'}</p>
  }

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

        <button type="button" onClick={() => void scanNow()} disabled={busy || state.scanning}>
          {state.scanning ? 'Looking…' : 'Scan now'}
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
    </li>
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
