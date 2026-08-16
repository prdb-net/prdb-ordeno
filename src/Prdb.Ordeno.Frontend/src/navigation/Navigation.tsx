import type { MouseEvent } from 'react'

import { areas, type AreaPath } from './areas'

/**
 * The areas, as links rather than buttons.
 *
 * A link is what they are: each one has an address, and a middle click or a
 * ctrl-click should open it in a second tab like any other link on the web.
 * That is what the modifier check below is for — everything else is handled
 * here rather than by a page load, because the tool's state and its polling
 * would otherwise be thrown away on every switch.
 */
export default function Navigation({
  area,
  onChosen,
}: {
  area: AreaPath
  onChosen: (area: AreaPath) => void
}) {
  const follow = (event: MouseEvent<HTMLAnchorElement>, to: AreaPath) => {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) {
      return
    }

    event.preventDefault()
    onChosen(to)
  }

  return (
    <nav className="areas" aria-label="Areas">
      {areas.map((one) => (
        <a
          key={one.path}
          href={one.path}
          className={one.path === area ? 'area chosen' : 'area'}
          aria-current={one.path === area ? 'page' : undefined}
          onClick={(event) => follow(event, one.path)}
        >
          {one.label}
        </a>
      ))}
    </nav>
  )
}
