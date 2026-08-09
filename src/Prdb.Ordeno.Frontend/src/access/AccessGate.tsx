import { useState } from 'react'

import { access, Refused, type AccessState } from '../api/client'

/**
 * The first thing anyone sees. On a fresh installation it sets the one password
 * (ADR 0010) and signs the visitor in by doing so; afterwards it asks for it.
 */
export default function AccessGate({
  passwordSet,
  onSignedIn,
}: {
  passwordSet: boolean
  onSignedIn: (state: AccessState) => void
}) {
  const [password, setPassword] = useState('')
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setProblem(null)

    try {
      onSignedIn(passwordSet ? await access.signIn(password) : await access.setPassword(password))
    } catch (error) {
      setProblem(error instanceof Refused ? error.message : 'Something went wrong.')
    } finally {
      setBusy(false)
      setPassword('')
    }
  }

  return (
    <form className="card" onSubmit={submit}>
      <h2>{passwordSet ? 'Sign in' : 'Set a password'}</h2>
      {passwordSet ? (
        <p className="hint">One password, no username — this installation's.</p>
      ) : (
        <p className="hint">
          This tool moves files that cannot be got back, so it does not run without a password.
          There is no default one: whoever opens it first sets it, and that is you.
        </p>
      )}

      <label>
        Password
        <input
          type="password"
          autoComplete={passwordSet ? 'current-password' : 'new-password'}
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          required
        />
      </label>

      {problem !== null && <p className="problem">{problem}</p>}

      <button type="submit" disabled={busy || password.length === 0}>
        {passwordSet ? 'Sign in' : 'Set the password'}
      </button>
    </form>
  )
}
