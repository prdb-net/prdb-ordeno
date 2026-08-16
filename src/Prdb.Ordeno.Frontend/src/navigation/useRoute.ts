import { useCallback, useEffect, useState } from 'react'

import type { SectionPath } from '../configuration/sections'
import { firstArea, isArea, sectionsOf, type AreaPath } from './areas'

/**
 * Which area is on screen, and which section of it where an area has them, as
 * the address bar says.
 *
 * Real paths rather than a hash, because the host already serves `index.html`
 * for anything that is not `/api` (`MapFallbackToFile` in Program.cs), so
 * reloading `/settings/library` works and the URL is one somebody can bookmark
 * or send. No router library: a fixed list of addresses two segments deep is a
 * `pushState`, a `popstate` listener and the split below, and a dependency here
 * would be carrying a routing table, a matcher and a link component for a list
 * that fits on one screen. The moment an address needs a parameter of its own —
 * a scene, a run in the log — is the moment to weigh a real one; a section
 * (ADR 0026) is static and is not one.
 */
export function useRoute(): {
  area: AreaPath
  section: SectionPath | null
  go: (area: AreaPath, section?: SectionPath) => void
  replace: (area: AreaPath, section?: SectionPath) => void
} {
  const [path, setPath] = useState(() => window.location.pathname)

  useEffect(() => {
    const onPopState = () => setPath(window.location.pathname)

    window.addEventListener('popstate', onPopState)

    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  const address = read(path)
  const known = address !== null

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

  const go = useCallback((area: AreaPath, section?: SectionPath) => {
    const to = addressOf(area, section)

    if (window.location.pathname === to) {
      return
    }

    window.history.pushState(null, '', to)
    setPath(to)
  }, [])

  /**
   * The same move without a history entry, for a correction the visitor did not
   * ask for — an installation that has not been set up yet is on the setup
   * whatever address it was opened at, and going back from there should leave
   * the tool rather than return to an address that showed the setup too. An
   * area landed on without a section corrects itself the same way.
   */
  const replace = useCallback((area: AreaPath, section?: SectionPath) => {
    const to = addressOf(area, section)

    if (window.location.pathname === to) {
      return
    }

    window.history.replaceState(null, '', to)
    setPath(to)
  }, [])

  return { area: address?.area ?? firstArea, section: address?.section ?? null, go, replace }
}

function addressOf(area: AreaPath, section?: SectionPath): string {
  return section === undefined ? area : `${area}/${section}`
}

/**
 * An address split into the area and the section within it, or null when it is
 * not one this workspace has.
 *
 * A section under an area that has none is as wrong as an area that does not
 * exist, and so is a third segment: both are corrected rather than shown as the
 * area with the rest quietly dropped, because an address that stays in the bar
 * while meaning something else is one somebody will send on.
 *
 * A *missing* section is not wrong here. `/settings` is the area's own address,
 * and what it should show instead depends on whether the setup has been
 * finished — which this hook does not know, and the workspace does.
 */
function read(path: string): { area: AreaPath; section: SectionPath | null } | null {
  const parts = path.split('/').filter((part) => part.length > 0)
  const area = `/${parts[0] ?? ''}`

  if (parts.length > 2 || !isArea(area)) {
    return null
  }

  const asked = parts[1]

  if (asked === undefined) {
    return { area, section: null }
  }

  const section = sectionsOf(area).find((one) => one.path === asked)

  return section === undefined ? null : { area, section: section.path }
}
