import type { MouseEvent } from 'react'

/** One entry: what it is called, where it points, and what to say it was. */
export type Link<T extends string> = {
  readonly to: T
  readonly href: string
  readonly label: string
}

/**
 * A row of places to go, as links rather than buttons.
 *
 * A link is what they are: each one has an address, and a middle click or a
 * ctrl-click should open it in a second tab like any other link on the web.
 * That is what the modifier check below is for — everything else is handled
 * here rather than by a page load, because the tool's state and its polling
 * would otherwise be thrown away on every switch.
 *
 * Both rows in the workspace are this component: the areas across the top, and
 * the settings' own sections under them. They differ in what they are called
 * and how loudly they are drawn, which is `kind`, and in nothing else — two
 * copies of the modifier check would be two chances to get it wrong.
 */
export default function Navigation<T extends string>({
  kind,
  label,
  links,
  chosen,
  onChosen,
}: {
  kind: 'area' | 'section'
  label: string
  links: readonly Link<T>[]
  chosen: T
  onChosen: (to: T) => void
}) {
  const follow = (event: MouseEvent<HTMLAnchorElement>, to: T) => {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) {
      return
    }

    event.preventDefault()
    onChosen(to)
  }

  return (
    <nav className={kind === 'area' ? 'areas' : 'sections'} aria-label={label}>
      {links.map((one) => (
        <a
          key={one.to}
          href={one.href}
          className={one.to === chosen ? `${kind} chosen` : kind}
          aria-current={one.to === chosen ? 'page' : undefined}
          onClick={(event) => follow(event, one.to)}
        >
          {one.label}
        </a>
      ))}
    </nav>
  )
}
