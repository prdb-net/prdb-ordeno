# 25. The workspace is navigated by URL

Date: 2026-08-15

## Status

Accepted.

## Context

The signed-in workspace started as one screen and grew into four things a person
does: look at what was found, decide what happens to it, settle what prdb could
not, and change the setup. Three of them shared a page — the downloads screen
carried the directories, what prdb had said, the filing plan, the result of the
last filing run and the list of files, in one column, in that order. The list
somebody came to read was below everything else, and filing — the only part of
the tool that moves a file — was a card in the middle of a page about something
else.

That page also had no address. The areas were React state, so a reload always
landed on the downloads, a link to "the review queue" could not be sent, and the
browser's back button left the application entirely.

Two more areas are already known to be coming: the operation log with the way
back out of a filing run (#19), and the library the tool has filed into. Neither
fits on a page that is already the longest one here.

## Decision

Each area of the workspace is a path, and the address bar says which one is on
screen: `/downloads`, `/filing`, `/review`, `/settings`. Filing is one of them
rather than a card under the downloads.

Real paths rather than a fragment, because the host already answers every
non-`/api` path with `index.html` — `MapFallbackToFile` in `Program.cs` — so a
reload of `/filing` works and the address is one somebody can bookmark or paste.

The router is `src/navigation/`, about sixty lines: the areas as a list,
`pushState` and a `popstate` listener, and links that a modifier click opens in a
new tab like any other link. No routing library. Four static paths and no
parameters do not need a matcher, and a dependency here would be code that has
to be updated for the lifetime of the repository in exchange for a list that
fits on one screen.

The areas are declared once, in `navigation/areas.ts`. The navigation, the
routing and the fallback for an unknown path all read that list, because three
copies would disagree the first time an area is added.

## Consequences

An unknown path replaces itself with the first area rather than pushing a
history entry nobody chose, so going back does not land on the address that was
just corrected.

Switching areas throws away the screen's state, including whatever it was
polling. That is the intended cost: a filing run and a scan both outlive the
request that started them and are reported by the server when the area is opened
again, so nothing is lost that the tool did not already know.

Adding an area is a line in `areas.ts` and a branch in `Workspace`. When one
needs a parameter of its own — a scene, a run in the log — this router does not
have one, and that is the moment to weigh a real one rather than now.

Deep links only work while the fallback does. A future deployment behind a proxy
that serves the frontend itself has to keep answering unknown paths with
`index.html`, and `docs/running-in-docker.md` is where that belongs if it ever
stops being true.
