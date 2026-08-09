# 0003. Duplicate scenes are skipped, never deleted

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

Two files can be the same scene. The library should not hold the same thing
twice, but "should not hold it twice" has two very different implementations:
refuse the second import, or remove one of the two.

The hard rule in `AGENTS.md` says destructive operations are opt-in and never
the default. Deleting the loser of a duplicate comparison on a default setting
would contradict that, however reasonable it sounds when described as
housekeeping.

## Decision

The default distinguishes by what differs:

- **Different quality** — both are kept. Someone holding both a 1080p and a
  2160p version usually has a reason.
- **Same scene, same quality** — the second file is not filed. It is left
  exactly where it is and reported.

Nothing is deleted. The user can change this in either direction.

## Alternatives considered

- **Delete the redundant copy.** Rejected as a default: skipping the import
  achieves what the user asked for without destroying anything, and the
  destruction cannot be undone by the operation log the way a move can.
- **Always keep everything.** Rejected: it makes the tool useless against the
  case it was pointed at, a download directory holding the same scene three
  times.

## Consequences

- Comparing quality means reading it out of the file itself. prdb identifies the
  scene, not the encode a particular person happens to have, so this is our job.
- "Same scene" is only knowable for files that were identified. Two unidentified
  files are never assumed to be duplicates of each other.
- Skipped files accumulate in the source directory and need to be visible in the
  UI, otherwise the user finds a download folder that never empties and no
  explanation why.
- `fulfilledInQuality` in prdb has three levels while we read real resolutions;
  reporting rounds.
