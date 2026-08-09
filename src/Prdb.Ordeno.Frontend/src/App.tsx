import { useEffect, useState } from 'react'

import type { components } from './api/schema'

// ADR 0014: the shape is the backend's, not a hand-made copy of it. Rename the
// field there without regenerating and this build is where it stops.
type Health = components['schemas']['HealthResponse']

type Backend =
  | { state: 'checking' }
  | { state: 'answering'; status: string }
  | { state: 'silent'; reason: string }

export default function App() {
  const [backend, setBackend] = useState<Backend>({ state: 'checking' })

  useEffect(() => {
    let abandoned = false

    const check = async () => {
      try {
        const response = await fetch('/api/health')
        if (!response.ok) {
          throw new Error(`the API answered ${response.status}`)
        }

        const health = (await response.json()) as Health
        if (!abandoned) {
          setBackend({ state: 'answering', status: health.status })
        }
      } catch (error) {
        if (!abandoned) {
          setBackend({
            state: 'silent',
            reason: error instanceof Error ? error.message : 'unknown error',
          })
        }
      }
    }

    void check()

    return () => {
      abandoned = true
    }
  }, [])

  return (
    <main>
      <h1>prdb-ordeno</h1>
      <p>
        There is no interface yet. This page exists so that the frontend the
        backend serves is demonstrably the frontend that was built.
      </p>
      <p>
        Backend:{' '}
        {backend.state === 'checking' && <span>asking…</span>}
        {backend.state === 'answering' && <span>{backend.status}</span>}
        {backend.state === 'silent' && <span>not answering — {backend.reason}</span>}
      </p>
    </main>
  )
}
