# 0029. Undo returns one operation or one run, and refuses rather than guessing

- **Status:** Accepted
- **Date:** 2026-08-19

## Context

[ADR 0028](0028-the-operation-log-records-what-changed-and-is-trimmed-by-whole-runs.md)
decided what the log records and why. This decides what reads it.

The case it exists for is named in `VISION.md` and again in
[#19](https://github.com/prdb-net/prdb-ordeno/issues/19): the unattended run
that filed two hundred files overnight under a rule that turned out to be wrong.
Anything that only undoes one file at a time fails that case, and anything that
undoes a batch by guessing fails the hard rule in `AGENTS.md` — this is a write
path like any other, and the fact that it is called "undo" makes it more
dangerous rather than less, because a user pressing it has already decided that
whatever it touches is not what they wanted.

The other half of the context is what filing actually does, since a reversal is
its mirror. One filing moves a video, may rename a file the library already held
([ADR 0020](0020-a-second-quality-relabels-the-filed-file.md)), may create the
scene directory, may write a `movie.nfo`
([ADR 0024](0024-the-sidecar-is-written-by-filing-and-only-over-its-own.md)) and
may write a `fanart.jpg`
([ADR 0027](0027-artwork-is-one-image-written-only-where-there-is-none.md)) —
and it deletes the row that said the file was in a download directory, taking
prdb's answer and any decision a person made about it with it.

## Decision

**The way back is the same code path as the way there.** Putting a video back is
a move like any other and goes through `LibraryMoves`: a rename where the tool
can prove one filesystem, and a copy, a verification and only then a delete
where it cannot ([ADR 0002](0002-files-are-moved-not-copied.md),
[ADR 0021](0021-a-copy-is-verified-by-size-and-os-hash.md)). A reversal that
took a shortcut the forward path is not allowed to take would be a way of losing
a file while claiming to give it back.

**Two units, and only two: one operation, and one run.** The operation is the
file somebody is looking at; the run is the overnight batch, which is the case
this feature exists for. There is no "undo everything since Tuesday" and no
selection of arbitrary rows — a range somebody assembles by hand is a plan
nobody reviewed, and a run is the batch a user actually remembers.

**A run is undone in reverse.** Last operation first, so that a second quality
goes back to the download directory before the file it relabelled is renamed
back — the pairing ADR 0020 requires, read backwards. The same rule applies
between runs: an operation whose file a later run has renamed is refused and the
message names that later run, because the way back out of it is to undo that one
first. Reverse chronological order is the whole rule, at both scales.

**What goes back, and what goes with it.** The video returns to the path it came
from. Then, and only after it has arrived: the `movie.nfo` this operation wrote
is removed if it still carries the marker ADR 0024 puts in it, the `fanart.jpg`
this operation wrote is removed if its bytes are still the ones ADR 0028
recorded a hash of, and the scene directory is removed if this operation created
it and nothing is left in it. Each of those conditions is a way of not deleting
somebody else's work, and each of them fails safely: the leftovers are named in
the report and the video is back either way.

A directory that still holds another copy of the scene keeps its sidecar and its
image, because they describe a video that is still there.

**A sidecar this tool replaced is not restored to what it said before.** The log
does not carry the old document, on purpose (ADR 0028). What a user is owed is
the file that was moved; what a sidecar said is what prdb said at the time, and
prdb is still there to say it again the next time something is filed into that
scene.

**An undo can be asked what it would do.** The hard rule applies here like
anywhere else, and it applies to the same computation: the checks below run
against the filesystem without touching it, the screen shows what would go back
and what would be refused with the reason, and the run makes those checks again
as it reaches each file. That second pass is ADR 0022's, for ADR 0022's reason —
a file can be renamed, replaced or removed in the seconds between reading a
screen and pressing a button, and undoing on the strength of a check that has
gone stale is exactly what the preview exists to prevent.

**Refusing rather than guessing.** An operation whose reversal is not plainly
safe does nothing at all — no partial attempt, no best effort — and says which
of these stopped it:

- It has been undone already. The record says so, which is a cheaper and more
  honest answer than the filesystem's.
- The file is not at the path the entry names. Something else has happened to it
  since, and the tool does not go looking.
- It is at that path and is not the file that was filed, by size or by the
  `osHash` the scan read before the move. A file that has changed since is
  somebody's work, whatever it is called.
- The directory it came from no longer exists. That is "the original location is
  gone", and it is common on this audience's storage: a share that is not
  mounted looks like an empty path, and putting two hundred files into a
  directory that is really a mountpoint is how a NAS fills its system disk.
- Something is already at the path it would go back to. A reversal that
  overwrites is not a reversal.

A download directory that still exists but is no longer one the tool watches
gets its file back all the same, and the report says so. Where the file belongs
is not a question about the tool's configuration.

**Partial undo is reported, never hidden.** Every operation in a run is checked
and carried out on its own, and one refusal does not stop the ones after it.
What comes back is the same shape the filing report has: how many went back, and
which did not with the reason under each. If a hundred and ninety of two hundred
go back, the user is told which ten did not and why.

**Undo runs behind the same gate as filing**, and is reported the same way: it
outlives the request that started it, the screen polls it, and a shutdown
reaches it. Two things rearranging one library at once is the state the gate has
always existed to prevent, and an undo is not a special case of anything.

**The media server is not told.** ADR 0018 keeps it out of the filing path, and
there is nothing useful to say to it here anyway: the targeted refresh it offers
is for an item whose sidecar changed, and an item whose file has gone is removed
by the server's own scan rather than refreshed. A connection that exists costs
nothing here and a connection that does not is unaffected, which is the rule
ADR 0018 asked for.

**A file that comes back is a file the tool has not seen yet.** The row that
said where it was, prdb's answer about it and any decision a person made were
deleted when it was filed, and the undo does not put them back. The ordinary
loop takes it from there: the next scan finds it, it settles, and it is asked
about once ([ADR 0017](0017-prdbs-answer-is-stored-and-asked-for-once.md)). The
price is one prdb request per undone file and, for a file somebody had settled
by hand in the review queue
([ADR 0023](0023-a-persons-answer-is-kept-beside-prdbs.md)), being asked to
settle it again. That is the honest cost of the smallest reversal that works,
and the alternative is a log that carries the tool's whole state per entry so
that undo can replay it.

**Nothing refiles what was just undone**, because nothing files anything without
being asked (ADR 0022). When the timer arrives it inherits this question, and it
has to answer it: an unattended run that refiles overnight what somebody undid
that evening is the way back cancelled by the feature it unblocked.

## Alternatives considered

- **Move the file to a trash directory instead of back.** Rejected. It is the
  same amount of work and it leaves the user with a second place to look and a
  directory that has to be swept by somebody; the download directory the file
  came from is the place it belongs, and it is the place the tool already knows
  how to find things in.
- **Keep a copy of every filed video for a while**, so an undo never has to move
  anything. Rejected outright: it doubles the storage of a tool whose files are
  measured in gigabytes, on hardware chosen for capacity.
- **Undo everything since a point in time.** Rejected in favour of the run. A
  timestamp is a boundary the user has to reason about and the tool cannot show
  them beforehand; a run is a thing that happened, with a report attached.
- **A force option that overwrites what is in the way.** Rejected. It is the one
  thing the hard rule forbids everywhere else, and the argument for it here —
  "the user knows what they are doing" — is exactly the argument the preview and
  the confirmation exist to refuse.
- **Restore the discovered file, prdb's answer and the person's decision from
  the log**, so an undone file is not asked about again. Rejected for this
  release, and it is the closest call here. It means the log carries a copy of
  three tables so that it can replay them, an undone file is instantly a
  candidate for filing again, and the saving is one prdb request per file. The
  loop that already exists produces the same answer for the same bytes.
- **Undo without a log, by finding the tool's own sidecars in the library.**
  Rejected. It finds only what the tool wrote a sidecar for, it cannot say where
  a file came from, and it turns a reversal into a search over somebody's whole
  library.
- **Let an undo delete the filed copy when the original location is gone.**
  Rejected. Deleting is not undoing, and the tool that promised a way back would
  be the one that removed the file.
- **Undo one file at a time only, and let a batch be two hundred clicks.**
  Rejected by the case in `VISION.md`. The batch is the reason the feature
  exists.

## Consequences

- A run that turned out to be wrong can be put back in one action, and what
  could not be put back is on the screen with a reason next to it rather than
  discovered later in a file manager.
- An undo is as slow as the filing was: the same bytes cross the same
  filesystems, verified the same way. A cross-mount batch takes as long to
  reverse as it took to file, and the screen says so while it runs.
- Files come back into the download directories and are identified again. A
  first undo of a large run costs a handful of prdb requests and puts anything
  that was settled by hand back into the review queue.
- The tool now removes files — a sidecar and an image it can prove it wrote, in
  a directory it created. That is new, it is the narrowest possible version of
  it, and every condition on it is checked at the moment of the removal rather
  than taken from the log alone.
- ADR 0022's timer becomes buildable, and inherits one open question: what an
  undone file means to an unattended run.
- [ADR 0020](0020-a-second-quality-relabels-the-filed-file.md)'s half undo is
  closed. A relabel is an operation, and it comes back with the file that caused
  it.
