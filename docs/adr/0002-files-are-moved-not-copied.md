# 0002. Files are moved, not copied or linked

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

Once a file is identified, it has to reach its place in the target structure.
Three shapes are possible: move it, copy it, or leave the original where it is
and create a hard link in the target.

Hard-linking is what the \*arr tools do, and users coming from them expect it: a
link costs no extra disk, and the download client keeps its file where it left
it, so its history and any post-processing stay intact.

## Decision

Files are moved. The download directory is meant to end up empty.

## Alternatives considered

- **Hard-link into the target, leave the source in place.** Rejected: tidiness
  on disk is part of what the user came for. A tool that leaves the download
  directory exactly as full as it found it has solved the media server's problem
  and made the storage problem worse. Hard links also fail silently as an idea
  across filesystems, which is the common NAS setup.
- **Copy and leave the original.** Rejected for the same reason, plus it doubles
  the space.

## Consequences

- Source and target should sit on the same filesystem. Within one, a move is
  instant and cannot half-happen; across two it is a copy, a verification and a
  delete — slow, and the only place a file can be lost to a crash. Both must
  work, but the documentation has to make the difference visible so the fast
  path is chosen knowingly rather than discovered by watching a progress bar.
- Cross-filesystem moves are copy-then-verify-then-delete, never a rename that
  silently degrades into a copy. See the hard rule in `AGENTS.md`.
- Anything that assumed the original stays put — a download client still seeding
  or post-processing — is the user's to arrange. Worth saying in the docs.
- Because the move is destructive from the source's point of view, the operation
  log and its undo (`VISION.md`) are not optional extras; they are the
  counterweight to this decision.
