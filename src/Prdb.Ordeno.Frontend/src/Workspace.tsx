import { useCallback, useEffect, useState } from 'react'

import { configuration as api, Refused, SignedOut, type ConfigurationState } from './api/client'
import ConfigurationScreen from './configuration/ConfigurationScreen'
import FilingScreen from './library/FilingScreen'
import Navigation from './navigation/Navigation'
import { useRoute } from './navigation/useRoute'
import ReviewScreen from './review/ReviewScreen'
import ScanScreen from './scanning/ScanScreen'

/**
 * What a signed-in visitor sees. Until the setup is finished there is only the
 * setup — ADR 0009 has the tool doing nothing until then, and a navigation
 * offering areas that would all be empty is a way of hiding that.
 */
export default function Workspace({ onSignedOut }: { onSignedOut: () => void }) {
  const [configuration, setConfiguration] = useState<ConfigurationState | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const { area, go, replace } = useRoute()

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

  // An installation that has not been set up yet shows the setup whatever
  // address it was opened at, so the address is corrected to the one that is
  // actually on screen. The setup is the settings — the same screen — which is
  // why there is no fifth area for it.
  const setUp = configuration?.complete === true

  useEffect(() => {
    if (configuration !== null && !setUp) {
      replace('/settings')
    }
  }, [configuration, setUp, replace])

  if (configuration === null) {
    return (
      <p className={problem === null ? 'hint' : 'problem'}>
        {problem ?? 'Asking the tool what it knows…'}
      </p>
    )
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
      <Navigation area={area} onChosen={go} />

      {area === '/downloads' && <ScanScreen onSignedOut={onSignedOut} />}
      {area === '/filing' && <FilingScreen onSignedOut={onSignedOut} />}
      {area === '/review' && <ReviewScreen onSignedOut={onSignedOut} />}
      {area === '/settings' && settings}
    </>
  )
}
