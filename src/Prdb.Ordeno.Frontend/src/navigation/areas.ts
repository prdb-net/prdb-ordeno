import { sections, type Section } from '../configuration/sections'

/**
 * The workspace's areas, in the order they are shown and in the order somebody
 * meets them: what was found, what would happen to it, what the tool could not
 * settle on its own, and how it is set up.
 *
 * One list, because the navigation, the routing and the fallback all have to
 * agree about what exists — three copies of that would disagree the first time
 * an area is added.
 *
 * An area may carry sections, which is the same list one level down: the
 * settings are four separate things to change rather than one long column, and
 * `/settings/library` is an address like any other here. Only the settings have
 * them, and an area without them is an area with one page.
 */
export const areas = [
  { path: '/downloads', label: 'Downloads' },
  { path: '/filing', label: 'Filing' },
  { path: '/review', label: 'Review' },
  { path: '/settings', label: 'Settings', sections },
] as const

export type Area = (typeof areas)[number]
export type AreaPath = Area['path']

/** Where an unknown path, and the bare root, end up. */
export const firstArea: AreaPath = '/downloads'

export function isArea(path: string): path is AreaPath {
  return areas.some((area) => area.path === path)
}

/** What an area is divided into, which for most areas is nothing. */
export function sectionsOf(area: AreaPath): readonly Section[] {
  const one = areas.find((candidate) => candidate.path === area)

  return one !== undefined && 'sections' in one ? one.sections : []
}
