# 0026. An area may have sections, and the settings do

- **Status:** Accepted
- **Date:** 2026-08-16
- **Amends:** [ADR 0025](0025-the-workspace-is-navigated-by-url.md)

## Context

[ADR 0025](0025-the-workspace-is-navigated-by-url.md) gave each area of the
workspace an address and said the router needs no matcher, because there are
"four static paths and no parameters".

One of those areas is doing two jobs. The settings screen is also the guided
path of [ADR 0009](0009-configuration-is-collected-by-onboarding.md) — one
screen, because it is one configuration — and the shape that path needs is a
column: the API key, the download directories, the library, the media server,
under each other, numbered, each appearing as the one before it is answered.
What is left to do is visible without clicking anything, which is the whole
point of a guided path.

Afterwards nobody reads that column. Somebody who opens the settings came to
change one thing, and the column makes them scroll past three others to reach
it. It also only grows: ADR 0009 promises the behaviour switches around
duplicates and contributions, and [ADR 0010](0010-one-password-set-at-first-run.md)
a password that can be changed. At eight blocks the column is a page nobody
navigates, and the address bar still says `/settings` wherever they are in it —
"the media server settings" is not a link somebody can send, which is the
complaint ADR 0025 was written to answer one level up.

## Decision

An area may be divided into sections, and a section is a path segment under it.
The settings have four, one per thing there is to change: `/settings/prdb`,
`/settings/sources`, `/settings/library`, `/settings/media-server`. One of them
is on screen at a time.

Sections are static, like areas. An address is at most two segments and carries
no parameter, so this remains a `split` and a `pushState` rather than a matcher,
and ADR 0025's decision to take no routing library is unchanged. So is its
reason to revisit that: an address needing a parameter of its own — a scene, a
run in the log — is still the moment to weigh a real router, and a section is
not one.

The sections are declared once, in `configuration/sections.ts`, which
`navigation/areas.ts` reads. Same rule as the areas, one level down: the
navigation, the routing and the correction of an address nobody has must agree
about what exists.

The guided path is untouched. Before onboarding is finished the settings are
one column, numbered, with no sections in it, because there the order is the
point. The numbers go with it — a number answers "how much is left", and a page
showing one block is not asking.

`/settings` on its own therefore means two things, and the workspace decides
which, not the router: the guided path before onboarding is finished, and a
replace to the first section after it. The router cannot make that call, because
whether the setup is done is not something an address knows.

## Alternatives considered

- **A jump list of anchors at the top of the column.** Rejected. It is the
  cheapest change and keeps browser find working across every setting, but it
  gives no addresses — the complaint above — and on a page that only grows it
  becomes a longer table of contents rather than a way around.
- **Collapsing the blocks, one open at a time.** Rejected for the same missing
  address, and it costs more than it looks: browser find stops reaching what is
  collapsed, so the page gets harder to search rather than easier.
- **Sections for every area.** Rejected. Only the settings hold several
  independent things; a second level under Downloads would be a menu with one
  entry in it, kept alive for symmetry.
- **Edit ADR 0025.** Rejected for the reason
  [ADR 0015](0015-tests-may-reference-the-host.md) gives: decisions here are
  amended by new ones rather than rewritten. Nothing in 0025 turned out wrong —
  its address space was one level shallower than what the settings needed.

## Consequences

- The router reads two segments. A section under an area that has none, and a
  third segment, are corrected exactly like an area that does not exist: an
  address that stays in the bar while meaning something else is one somebody
  will send on.
- A *missing* section is not an error there, which is the one asymmetry in the
  router and is deliberate. `/settings` is the area's own address; what it should
  show depends on the configuration, which the workspace has and the router does
  not.
- Both rows of links are one component. The modifier check that lets a middle
  click open an area in a new tab is the part worth getting right, and there is
  one of it.
- A new setting gets a section rather than a place at the bottom of a column:
  a line in `sections.ts` and a branch in `ConfigurationScreen`.
- Switching sections throws away that block's local state — a half-typed path,
  a message from the last refusal. Cheaper than it sounds, and the same trade
  ADR 0025 already made between areas: what was actually stored comes back from
  the server with the configuration.
