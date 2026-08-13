import { useCallback, useEffect, useState } from 'react'

import AccessGate from './access/AccessGate'
import { access, Refused, type AccessState } from './api/client'
import Workspace from './Workspace'

type Gate =
  | { state: 'asking' }
  | { state: 'answered'; access: AccessState }
  | { state: 'silent'; reason: string }

export default function App() {
  const [gate, setGate] = useState<Gate>({ state: 'asking' })

  const ask = useCallback(async () => {
    try {
      setGate({ state: 'answered', access: await access.state() })
    } catch (error) {
      setGate({
        state: 'silent',
        reason: error instanceof Refused ? error.message : 'The tool is not answering.',
      })
    }
  }, [])

  useEffect(() => {
    void ask()
  }, [ask])

  const signOut = async () => {
    await access.signOut().catch(() => undefined)
    await ask()
  }

  return (
    <main>
      <header>
        <h1>prdb-ordeno</h1>
        {gate.state === 'answered' && gate.access.authenticated && (
          <button type="button" className="quiet" onClick={() => void signOut()}>
            Sign out
          </button>
        )}
      </header>

      {gate.state === 'asking' && <p className="hint">Asking the tool…</p>}
      {gate.state === 'silent' && <p className="problem">{gate.reason}</p>}

      {gate.state === 'answered' &&
        (gate.access.authenticated ? (
          <Workspace onSignedOut={() => void ask()} />
        ) : (
          <AccessGate
            passwordSet={gate.access.passwordSet}
            onSignedIn={(state) => setGate({ state: 'answered', access: state })}
          />
        ))}
    </main>
  )
}
