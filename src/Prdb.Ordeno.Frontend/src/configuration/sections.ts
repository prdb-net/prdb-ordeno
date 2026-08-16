/**
 * ADR 0026: the settings, cut into the four things somebody comes back to
 * change — the key that makes identification possible, where files arrive,
 * where they go, and the optional media server.
 *
 * These are the blocks the guided path walks through, in the same order — one
 * configuration shown twice. The path shows them one under another as they are
 * answered, because there the order is the point; afterwards each one has an
 * address of its own, so that "the media server settings" is a link somebody
 * can send rather than a place to scroll to.
 *
 * Declared here rather than in the screen, for the reason `navigation/areas.ts`
 * is a list too: the navigation, the routing and the correction of an address
 * nobody has all have to agree about what exists.
 */
export const sections = [
  { path: 'prdb', label: 'prdb' },
  { path: 'sources', label: 'Sources' },
  { path: 'library', label: 'Library' },
  { path: 'media-server', label: 'Media server' },
] as const

export type Section = (typeof sections)[number]
export type SectionPath = Section['path']

/** Where `/settings` on its own lands, once there is a setup to have sections. */
export const firstSection: SectionPath = 'prdb'
