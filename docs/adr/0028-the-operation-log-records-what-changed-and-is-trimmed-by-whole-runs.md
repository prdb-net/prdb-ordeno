# 0028. The operation log records what changed, and is trimmed by whole runs

- **Status:** Accepted
- **Date:** 2026-08-19

## Context

`VISION.md` puts the log and its undo in the first release and gives the reason:
*a version of this that identifies beautifully but leaves the user without a way
back is not a smaller release, it is an unusable one.*
[ADR 0022](0022-filing-happens-when-it-is-asked-for.md) took that literally and
made the filing timer wait for this, so the log is also what unblocks the
unattended running the tool exists for.

Three things about it are already decided elsewhere, and they fix the shape this
has to have.

**It is not the `FiledVideos` table.** That table says what is true of the
library *now*, holds nothing filing does not read, and deletes a row whose file
has gone — [ADR 0020](0020-a-second-quality-relabels-the-filed-file.md) and
`FiledVideo` both say the log lands next to it rather than out of it. History
and current truth in one table would mean one of the two lying.

**A relabel is a step of its own.** ADR 0020 is explicit: an undo that returns
the newcomer to the download directory and leaves the file it relabelled under
its new name is a half undo.

**An image has no marker in it, and a sidecar does.**
[ADR 0024](0024-the-sidecar-is-written-by-filing-and-only-over-its-own.md) tells
the tool's own `movie.nfo` from somebody else's by a comment inside the
document.
[ADR 0027](0027-artwork-is-one-image-written-only-where-there-is-none.md)
deliberately gave `fanart.jpg` no marker, because a tool that never writes over
an image does not have to recognise its own work — and rejected keeping a record
of the images written *as a way of deciding writes*. Taking one away again is a
different question, and nothing answers it yet.

What is left for this decision is what an entry is, what it says, and how a log
nobody will ever prune stays small enough to sit in the same SQLite file as
everything else after three years.
[#19](https://github.com/prdb-net/prdb-ordeno/issues/19) names that last part as
something to settle here rather than discover later.

## Decision

**An entry is one change to a filesystem, and nothing else is an entry.** There
are two: a video moved into the library, and a file already in the library
renamed to carry its quality (ADR 0020). What was skipped, already filed,
blocked or failed changed nothing on disk and is not an entry — those files are
exactly where their owner left them, and a log that lists them is an activity
feed that grows with the size of the download directory instead of with what the
tool did.

**Entries belong to runs, and the run is the unit of the way back.** A run row
is opened when filing starts and closed when it stops: when it began, when it
finished, the one-line account of what it did — the sentence `FilingRun` already
builds for the screen, which is where "nineteen filed, one could not be moved"
comes from — and the problem that stopped it, if one did. The overnight batch is
a run, because undoing two hundred files one row at a time is not a way back.

**A run that moved nothing still leaves its row.** It costs one row and it is
the answer to the question somebody who was asleep actually asks, which is not
"what happened" but "did anything happen". A tool that says nothing about the
night it found nothing to do is a tool whose silence has two meanings.

**Every entry says why the tool believed the move was right.** prdb's id for the
scene and the title, site and date it was filed under; the rung that identified
it and the confidence that came with it; and, where a person settled it in the
review queue ([ADR 0023](0023-a-persons-answer-is-kept-beside-prdbs.md)), that a
person did and when. This is the half of the log that is not about undo at all:
it is what turns "it put my file in the wrong place" into something answerable,
and it has to be recorded at the time because the identification row it came
from is deleted with the file it described.

**And every entry says what the move was**, in enough detail for a reversal to
be safe rather than hopeful: where the file came from and where it went, whether
that was a rename or a copy-verify-delete
([ADR 0002](0002-files-are-moved-not-copied.md)), its size and its `osHash` as
the scan read them, whether the scene directory was created by this operation,
and what was written next to the video — the `movie.nfo`, and the `fanart.jpg`
with the length and hash of the bytes that were written.

That last field is the part no earlier decision provides, and it is worth being
exact about what it is for. It does not reopen ADR 0027: nothing reads it to
decide whether to write an image, and the answer to that stays "never over
anything". It answers the question a reversal has instead — not "is this image
mine to overwrite", which it never is, but "is this still the file this run put
here, so that removing it removes nothing somebody else did". A hash answers
that and nothing else, it is written once and read only by an undo, and without
it the honest behaviour would be to leave every image behind and tell the user
to go looking.

**The log is written by the run, from the plan the run carried out.** ADR 0022
made the preview and the run one computation and said the plan the preview
showed "becomes the thing the log records"; this is that sentence spent. Nothing
re-derives what happened from the filesystem afterwards.

**An entry is written in the same transaction as the row that says the library
holds the file, and with the same cancellation token: none.** `FilingService`
already writes `FiledVideos` whatever the shutdown is doing, because a library
holding a video no row knows about is worse than a late shutdown. A file the
tool moved and did not log is worse than both — it is the one file with no way
back, and it is created by exactly the interruption undo exists for.

**The log is trimmed by whole runs, oldest first, until at most 20,000 entries
and at most 1,000 runs remain.** Never inside a run: a run in the log can be
undone as a run, or it is not in the log at all. Half a batch is the one state
worth ruling out, because it is the state that looks complete and is not.

The trim happens where the run row is closed — one delete after a run, and never
in the middle of one — so an installation that files nothing writes nothing.

The numbers are arithmetic rather than taste. An entry is a few hundred bytes,
almost all of it paths, so the cap is something under ten megabytes: noticeable
next to a fresh database and small next to one already holding a row per file
for a hundred thousand downloads. Twenty thousand entries is a hundred
two-hundred-file nights; a thousand runs is the best part of three years of a
nightly one. Both are far past the horizon where an entry is still a way back
rather than a souvenir — the download directory an old entry names has usually
been unmounted, renamed or emptied long before.

**A cap rather than an age**, and this is the one place where the obvious answer
is the wrong one. An age bounds nothing: the week somebody points the tool at a
NAS full of two hundred thousand files fits inside every age window there is,
and that is precisely the week the log is largest and the undo most likely to be
wanted. A count bounds the file whatever the usage looks like. Nothing is
compacted, either — an entry that has been folded into a number is an entry that
can no longer be undone or quoted in a bug report, which is two of its three
jobs.

**An undo is recorded, and is not itself undoable.** The undo of a run is a run
of its own, with its own row and its own one-line account, and every entry it
reverses is stamped with when it was undone and by which run. So the screen
never shows a filing that appears to have happened while the library disagrees,
and a second attempt at the same entry is refused by the record rather than by
the filesystem. There is no undo of an undo: the way back out of one is filing
again, which is the button that already exists.

**One screen, at `/history`, newest first**: runs, each opening onto the entries
it wrote, with the reason and the paths under each. It is the same table shape
as the other areas ([ADR 0025](0025-the-workspace-is-navigated-by-url.md)) and
it keeps to one address.
[ADR 0026](0026-an-area-may-have-sections-and-the-settings-do.md) named "a run
in the log" as the kind of address that would need a parameter and therefore a
real router; that is deliberately not taken here, and it stays the moment to
weigh one.

## Alternatives considered

- **Log everything a run considered**, skipped and blocked files included, as an
  activity feed. Rejected. It is a different feature wearing this one's name:
  the entries that move nothing cannot be undone, they are already on the filing
  screen while they matter, and they would make the size of the log a function
  of how full the download directories are rather than of what the tool did.
- **History columns on `FiledVideos`.** Rejected by ADR 0020 before this was
  written, and it gets worse on contact: that table deletes the row when the
  file goes, which is the exact moment the history becomes interesting.
- **Trim by age — ninety days, a year.** Rejected on the arithmetic above. It
  reads as the kinder rule and it is the one that fails in the only case that
  matters.
- **Compact old runs into a summary row.** Rejected. It keeps the part of the
  log nobody needs — that something happened — and throws away the parts that do
  the work: what moved where, and enough about it to put it back.
- **Write the log to a file next to the database**, one line of JSON per
  operation. Tempting because it never blocks a writer, and rejected: SQLite is
  where local state lives
  ([ADR 0007](0007-sqlite-through-ef-core-for-local-state.md)), an undo has to
  read and stamp entries transactionally, and a second store means a second
  thing to back up, migrate and truncate.
- **Store the plan as a JSON blob per entry** rather than columns. Rejected: the
  screen filters and the undo reads individual fields, and a blob makes both a
  scan of the whole table. The fields are known — they are what the plan already
  carries.
- **Keep the previous `movie.nfo` in the log so an undo can restore it.**
  Rejected. A sidecar this tool replaced held what prdb said the last time, and
  prdb is still there; what an undo owes the user is the file it moved, not a
  restored copy of a description that can be fetched again. Storing documents
  would also put arbitrarily many kilobytes per entry against a cap that exists
  to keep the file small.
- **Record which run a `FiledVideos` row came from**, and read the log through
  it. Rejected: it couples the table that must stay minimal to the one that
  grows, and the join answers nothing the entry does not already say.

## Consequences

- Filing writes two rows per moved file instead of one, in the same transaction
  it already opens. There is no new I/O in the move itself.
- The tool can answer "why is this file here" months later, for a file whose
  identification row is long gone. That answer is what a bug report about a
  misfiled video should quote.
- The log knows which `fanart.jpg` it wrote, which is what makes an undo able to
  take one away without ever touching an image somebody else put there. Writes
  are unchanged: ADR 0027's rule that nothing is ever written over is not
  weakened, and nothing reads this field to decide one.
- An installation that has filed more than twenty thousand videos can no longer
  undo the oldest of them. That is the deliberate trade in
  [#19](https://github.com/prdb-net/prdb-ordeno/issues/19): the alternative is a
  file that grows without limit on a NAS whose owner will never prune it, and an
  entry that old names a download directory that has usually stopped existing.
- The filing timer ADR 0022 deferred now has somewhere to report to. What it
  needs beyond this is a way to say who asked for a run, which is a column and a
  decision for the issue that adds the timer, not a shape change here.
- The screen is a fifth area and one more line in `areas.ts`, which is what
  ADR 0025 built the list for.
