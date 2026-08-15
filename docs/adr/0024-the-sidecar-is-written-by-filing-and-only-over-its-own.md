# 0024. The sidecar is written by filing, and only ever over its own

- **Status:** Accepted
- **Date:** 2026-08-15

## Context

[`docs/jellyfin-layout.md`](../jellyfin-layout.md) settles what a sidecar looks
like: `movie.nfo`, root element `<movie>`, a `<premiered>` that is parsed against
exactly one format, an `<actor>` that has to have a `<name>` child and a `<type>`
Jellyfin knows, and a document that is discarded whole if a title put an
unescaped `&` in it. That is the shape, and it is measured rather than read out
of the documentation.

Building the writer ([#18](https://github.com/prdb-net/prdb-ordeno/issues/18))
asks three questions none of the earlier decisions answer.

**When is one written?** `VISION.md` leaves how much of a refresh happens on its
own as an open question, and [ADR 0022](0022-filing-happens-when-it-is-asked-for.md)
says nothing is filed until somebody asks. Neither says whether a sidecar is a
thing filing does or a thing of its own.

**How does the tool tell its own from somebody else's?** The layout document
notes the awkward part: a hand-written sidecar is most likely to be at exactly
the name this tool wants, because `movie.nfo` is what a Movies library reads.
There is no stepping around it the way a colliding directory name is stepped
around, and the hard rule in `AGENTS.md` says overwriting is something a user
turned on deliberately rather than something that happens because nobody turned
it off.

**What is it written from?** [ADR 0017](0017-prdbs-answer-is-stored-and-asked-for-once.md)
stores prdb's answer per file and asks once. That row is months old by the time a
second quality arrives, and `VISION.md` names the case directly: prdb corrects a
title, a date or a cast entry, and the file written last spring still says the
old thing.

## Decision

**A sidecar is written as part of filing a video, and at no other time.** It goes
in next to what moved, after it has moved. There is no timer, no sweep over the
library, and nothing is written for a scene the run did not file — including one
whose sidecar has gone missing. *When* an existing sidecar is refreshed is still
open, and this decision deliberately leaves it open; what it settles is that the
only thing writing one today is a filing somebody asked for.

**After the move, never before it.** Two reasons, and the second is the one that
bites: a sidecar written first leaves metadata in a directory holding no video if
the move then fails, and a directory with anything at all in it counts as
occupied — so the scene's own directory would look like somebody else's to the
run that tried again, and the scene would be filed around it under a name
carrying prdb's id, for a reason nobody could see.

**A sidecar the tool wrote carries a marker, and anything without one is left
alone.** The marker is an XML comment reading `Written by prdb-ordeno`, put at the
top of the document. A file that does not carry it is somebody's own work: the
video is filed next to it and the file is not touched. A file that cannot be
read is treated the same way, because the alternative is writing over something
on the strength of not having been able to look at it.

Deleting that comment line is how a user says "this one is mine now", and it is
deliberately something they can do in a text editor with no setting to find.

The comment is the one thing in the document `docs/jellyfin-layout.md` never
measured, so it was measured rather than assumed: a document from this writer,
carrying the marker and a title with `&`, `<`, `>` and an em dash in it, was put
in front of Jellyfin 10.11.11 through the probe harness. It came back as a
`Movie` with that title, premiere date 2025-11-07, production year 2025, the
studio, the provider id under the key `prdb`, and both performers as people of
type `Actor`. Rewriting it through the writer and rescanning past the one-minute
tolerance replaced the title, which is section 7 holding for a write and a rename
this tool made.

**What it says is fetched when it is written.** The identification row is for
putting a name on a screen; the sidecar is asked for again, through the same
batch lookup the review queue uses (`POST /videos/batch`, fifty at a time), once
per run rather than once per file. A lookup that fails or comes back without a
title writes nothing at all — no partial document, no placeholder — and the
video is still filed, because that decision was made from what the tool already
knew.

**Replacing one is a write and a rename.** The document is written to a dotted
temporary name in the same directory, flushed to disk, and renamed over the old
one. Section 7 of the layout document measured that Jellyfin does not care which
way the change arrived, so the reason is ours rather than the server's: a
container killed halfway through a truncating write leaves a document that parses
nowhere, and Jellyfin discards an unparseable sidecar in silence and falls back to
the file name.

**The fields are the ones prdb has an answer for**: `<title>`, `<premiered>` where
there is a release date, `<studio>` from the site, one `<actor>` per performer,
and `<uniqueid type="prdb">`. A plot, a genre and a runtime are not written
because prdb does not have them, and a field invented here is a field the media
server believes.

**Artwork is not part of this.** #18 allowed for it as a small addition; it is
not one. The images are URLs on prdb's CDN, so writing them means downloading
them, a setting to turn that on, and a decision about what a failed download
leaves behind — and section 5's measurements are about image files already on
disk, not about URLs in a sidecar. It becomes its own issue.

## Alternatives considered

- **Mark the tool's own sidecar with an element rather than a comment.**
  Rejected. An element is a field the server tries to read, and what an unknown
  one does was not measured — the whole point of the layout document is that this
  server does surprising things with fields it does not recognise. Every XML
  parser skips comments, which makes this the one addition that cannot change how
  the document is read.
- **Keep the marker in a separate hidden file.** Rejected. Two files to keep in
  step, one of which a user copying a directory takes and the other of which they
  do not — and the question "is this file mine" would then be answered by
  something other than the file.
- **Write a sidecar only where there is none, and never replace one.** Rejected.
  It makes the corrected title impossible, which is the case `VISION.md` names as
  the reason the sidecar is worth writing at all.
- **Replace whatever is there.** Rejected outright by the hard rule. A
  hand-written `movie.nfo` is work somebody did, it is at the only name that
  works, and losing it is not recoverable.
- **Write from the stored identification row.** Rejected. It is free and it is
  wrong: the row is a copy of an answer given when the file was first seen, and
  the sidecar exists to carry what is true now. `AGENTS.md` already says the
  stored copy is for reading.
- **Ask prdb per file rather than per run.** Rejected. A first pass over an
  existing library is thousands of files and a rate-limited quota; a batch of
  fifty is the same request pattern identification already uses.
- **Ask prdb while working out the preview.** Rejected. A preview is safe to
  press and may be pressed repeatedly, and spending quota to produce a sentence
  the user may not act on is the pattern
  [ADR 0001](0001-identification-runs-in-prdb.md) exists to avoid. The preview
  says a sidecar would be written; the run finds out what goes in it.
- **Refresh the sidecars of scenes the run found already filed.** Rejected here
  rather than on the merits — that is the refresh policy, and it needs its own
  decision. A run that reports having moved nothing must not have been writing.
- **Write prdb's image URLs as `<thumb>` elements.** Rejected for now. It is
  cheap to write and it makes the server fetch images from the internet, which is
  precisely what a user who left artwork off did not ask for.

## Consequences

- A video filed by this tool shows up in the library with its title, date, studio
  and cast, and the performers are people rather than text.
- A scene filed before a correction keeps the old sidecar until something files
  into that scene again. That is the gap the refresh decision closes, and until it
  exists the honest description of the feature is "written when a video is filed".
- Filing now spends prdb requests: one per fifty videos in a run. A run whose
  lookup fails still files, and every row says why it has no metadata next to it.
- A hand-written `movie.nfo` survives everything the tool does, and the user has a
  documented way to hand a file back: delete the marker line.
- The tool can find its own sidecars later — for a refresh, for a re-file under a
  changed layout, for the operation log
  ([#19](https://github.com/prdb-net/prdb-ordeno/issues/19)) — by the same marker,
  without keeping a second record of which files it wrote.
