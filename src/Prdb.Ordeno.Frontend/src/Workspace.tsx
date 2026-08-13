import { useCallback, useEffect, useState } from 'react'

import { configuration as api, Refused, SignedOut, type ConfigurationState } from './api/client'
import ConfigurationScreen from './configuration/ConfigurationScreen'
import ScanScreen from './scanning/ScanScreen'

type View = 'downloads' | 'settings'

/**
 * What a signed-in visitor sees. Until the setup is finished there is only the
 * setup — ADR 0009 has the tool doing nothing until then, and a navigation
 * offering screens that would all be empty is a way of hiding that.
 */
export default function Workspace({ onSignedOut }: { onSignedOut: () => void }) {
  const [configuration, setConfiguration] = useState<ConfigurationState | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [view, setView] = useState<View>('downloads')

  const read = useCallback(async () => {
    try {
      setConfiguration(await api.read())
    } catch (error) {
      if (error instanceof SignedOut) {
        onSignedOut()
        return
      }

      setProblem(error instanceof Refused ? error.message : 'Something went wrong.')
    }
  }, [onSignedOut])

  useEffect(() => {
    void read()
  }, [read])

  if (configuration === null) {
    return <p className={problem === null ? 'hint' : 'problem'}>{problem ?? 'Asking the tool what it knows…'}</p>
  }

  const settings = (
    <ConfigurationScreen
      initial={configuration}
      onChanged={setConfiguration}
      onSignedOut={onSignedOut}
    />
  )

  if (!configuration.complete) {
    return settings
  }

  return (
    <>
      <nav className="views">
        <button
          type="button"
          className={view === 'downloads' ? 'view chosen' : 'view'}
          onClick={() => setView('downloads')}
        >
          Downloads
        </button>
        <button
          type="button"
          className={view === 'settings' ? 'view chosen' : 'view'}
          onClick={() => setView('settings')}
        >
          Settings
        </button>
      </nav>

      {view === 'downloads' ? <ScanScreen onSignedOut={onSignedOut} /> : settings}
    </>
  )
}
