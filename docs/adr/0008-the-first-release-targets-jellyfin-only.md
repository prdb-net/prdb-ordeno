# 0008. The first release targets Jellyfin only

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

`VISION.md` describes a small set of layouts, one per supported media server,
because the point of the tool is that the library works in Jellyfin, Plex or
Emby — each with its own conventions for where files go and what the sidecars
are called. It also says the actual layouts and sidecar contents are a design
decision to be validated against a real library rather than a reading of the
docs.

Those two statements together are the problem. The first release is already the
whole loop — onboarding, identification, filing, sidecars, review queue,
operation log with undo — and validating three layouts means keeping three real
media servers with real libraries and checking each one's behaviour against what
we wrote. That is the most uncertain work in the release, multiplied by three,
at the point where the least is known about the code it sits in.

## Decision

The first release ships one layout: Jellyfin's, validated against a real
Jellyfin library. Plex and Emby follow afterwards, each on the same evidence.

## Alternatives considered

- **All three, as the vision reads.** Rejected as a v1 scope, not as a goal.
  Three layouts validated against documentation and none against a server is
  worse than one layout that demonstrably works — and Plex diverges most, so it
  is the one least safely guessed at.
- **Jellyfin and Emby together**, on the grounds that both read `.nfo` and the
  difference is small. Rejected for now: "the difference is small" is exactly
  the claim that needs a second real library to check, and if it turns out true
  Emby is a short follow-up rather than a saved week.
- **A configurable pattern language instead of fixed layouts.** Already ruled out
  in `VISION.md`, and this decision does not reopen it. Choosing a layout answers
  "what do you watch this in?", and one supported answer is still an answer.

## Consequences

- The tool says Jellyfin in its README, its onboarding and its release notes.
  Someone running Plex must find that out before installing, not after their
  library has been filed.
- Writing sidecars gets a seam from the first commit, but only one
  implementation behind it. The second server is what shapes that abstraction;
  inventing it now against two servers nobody has run means designing for
  guesses.
- Re-filing an existing library because the user switched layout — one of the
  long-run cases in `VISION.md` — has no second layout to switch to in v1. The
  path is still built, because switching *media servers* is precisely when a
  user needs it and it must not be retrofitted into a library that already
  exists, but it goes into the first release less exercised than the rest.
- `VISION.md`'s "one per supported media server" describes the end state. The
  supported set starts at one.
- This is a scope decision, not a technical one, and it expires on its own: it
  is superseded by shipping the second layout, not by another ADR.
