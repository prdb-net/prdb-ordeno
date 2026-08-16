import { useCallback, useEffect, useState } from 'react'

import { firstArea, isArea, type AreaPath } from './areas'

/**
 * Which area is on screen, as the address bar says it.
 *
 * Real paths rather than a hash, because the host already serves `index.html`
 * for anything that is not `/api` (`MapFallbackToFile` in Program.cs), so
 * reloading `/filing` works and the URL is one somebody can bookmark or send.
 * No router library: four static paths and no parameters is a `pushState` and a
 * `popstate` listener, and a dependency here would be carrying a routing table,
 * a matcher and a link component for a list that fits on one screen.
 */
export function useRoute(): {
  area: AreaPath
  go: (area: AreaPath) => void
  replace: (area: AreaPath) => void
} {
  const [path, setPath] = useState(() => window.location.pathname)

  useEffect(() => {
    const onPopState = () => setPath(window.location.pathname)

    window.addEventListener('popstate', onPopState)

    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  const known = isArea(path)

  // An unknown path — the bare root after signing in, a typo, a bookmark from
  // an area that no longer exists — becomes the first area without leaving a
  // history entry nobody chose. Replace rather than push, so that going back
  // does not land on the address that was just corrected.
  useEffect(() => {
    if (!known) {
      window.history.replaceState(null, '', firstArea)
      setPath(firstArea)
    }
  }, [known])

  const go = useCallback((area: AreaPath) => {
    if (window.location.pathname === area) {
      return
    }

    window.history.pushState(null, '', area)
    setPath(area)
  }, [])

  /**
   * The same move without a history entry, for a correction the visitor did not
   * ask for — an installation that has not been set up yet is on the setup
   * whatever address it was opened at, and going back from there should leave
   * the tool rather than return to an address that showed the setup too.
   */
  const replace = useCallback((area: AreaPath) => {
    if (window.location.pathname === area) {
      return
    }

    window.history.replaceState(null, '', area)
    setPath(area)
  }, [])

  return { area: known ? path : firstArea, go, replace }
}
