/**
 * The workspace's areas, in the order they are shown and in the order somebody
 * meets them: what was found, what would happen to it, what the tool could not
 * settle on its own, and how it is set up.
 *
 * One list, because the navigation, the routing and the fallback all have to
 * agree about what exists — three copies of that would disagree the first time
 * an area is added.
 */
export const areas = [
  { path: '/downloads', label: 'Downloads' },
  { path: '/filing', label: 'Filing' },
  { path: '/review', label: 'Review' },
  { path: '/settings', label: 'Settings' },
] as const

export type Area = (typeof areas)[number]
export type AreaPath = Area['path']

/** Where an unknown path, and the bare root, end up. */
export const firstArea: AreaPath = '/downloads'

export function isArea(path: string): path is AreaPath {
  return areas.some((area) => area.path === path)
}
