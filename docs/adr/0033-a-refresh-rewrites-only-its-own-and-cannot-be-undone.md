# 0033. A refresh rewrites only its own, fills in what is missing, and cannot be undone

- **Status:** Accepted
- **Date:** 2026-08-19

## Context

[ADR 0032](0032-a-refresh-is-a-run-of-its-own-over-what-the-tool-filed.md)
decides the run: what starts it, what it is over, what it costs and what bounds
it. It leaves what the run may actually write to here, because that question is
answered against two decisions it must not weaken and one it cannot satisfy.

[ADR 0024](0024-the-sidecar-is-written-by-filing-and-only-over-its-own.md) is
the first: the tool writes over a `movie.nfo` carrying its marker and over
nothing else, a rewrite is a write and a rename, and a lookup that fails writes
nothing at all. A refresh must not become a second path with softer rules — it
is a bulk operation over files a user considers finished, which makes it the run
where softer rules would do the most damage.

[ADR 0027](0027-artwork-is-one-image-written-only-where-there-is-none.md) is the
second, and it left the question pointing here: *"If a refresh is ever allowed
to replace an image, that is an amendment to this decision and needs its own
reasoning rather than arriving as a side effect."*

The third is the operation log. #38 says plainly that a run changing two hundred
sidecars overnight is what
[#19](https://github.com/prdb-net/prdb-ordeno/issues/19) exists to make
reversible and legible — and a rewritten document has no way back, because
nothing keeps the old one. That has to be decided rather than left to fall out
of the implementation.

One fact does most of the work below: `MovieNfo` touches no filesystem and *the
same metadata always produces the same document*. It was written that way to
make the document testable apart from the writing, and it makes the document
usable as a comparison.

## Decision

**What a refresh writes is decided by comparing documents, not timestamps.** The
run builds the `movie.nfo` that prdb's answer produces now and compares it with
the bytes on disk. Different — or nothing there — and it is written. Identical
and nothing happens at all: no write, no rename, no touched timestamp, nothing
for the media server to re-read.

That is the whole trigger, and it is deliberately not `updatedAtUtc`. That field
is on the video row, and a cast entry is not on the video row: a performer
corrected in prdb's actor tables can leave it untouched, and a cast entry is one
of the three cases `VISION.md` names. It would also have to be stored per scene
and kept in step, and it would buy nothing in requests, because the only way to
read it is the answer that already carries the corrected title. The document is
the honest question — *does what is on disk still say what prdb says* — and it
is free once the answer is in hand.

**Whose the file is is asked of the file, before prdb is asked about the
scene.** `ISidecars.StateOf` already answers it: the marker means the tool's
own, anything else — including a document that could not be read — is somebody's
work and is left exactly where it is. A refresh looks at every scene in its
slice first and only asks prdb about the ones it could write to. That inverts
ADR 0024's ordering on purpose: filing asks about everything because working out
which files move means reading the header of every video twice, while here the
check is a directory read the run has to make anyway, and the request is the
scarce thing.

**A missing sidecar is written.** ADR 0024 says filing writes nothing for a
scene it did not file, *"including one whose sidecar has gone missing"*. This is
what now covers that: the scene is one of the tool's own rows, the name is
empty, and writing into an empty name destroys nothing. A user who deletes a
`movie.nfo` to ask for a fresh one gets it here rather than having to file into
the scene again.

**Everything else about the write is ADR 0024's, unchanged.** A dotted temporary
file in the same directory, flushed, renamed over the old one, because a
truncating write killed halfway leaves a document that parses nowhere and
Jellyfin discards an unparseable sidecar in silence. A lookup that fails writes
nothing and stops the run; an answer with no title writes nothing for that
scene; a video prdb no longer knows is omitted from the answer, and an omitted
video leaves its sidecar exactly as it is. A refresh never writes a partial
document and never writes a placeholder.

**Artwork participates exactly as far as ADR 0027 already allows, and this is
not an amendment to it.** With the setting on, a scene directory with no file at
`fanart.jpg` and an image in prdb's answer gets one, downloaded and written the
way filing writes it: bounded size, checked to be a whole JPEG, dotted temporary
name, renamed with no overwrite, and nothing left behind if it fails. A file
already at that name is left alone, whoever put it there. That covers the three
cases worth covering — artwork switched on after a library was filed, a scene
prdb had no image for at the time, and a user who deleted one to ask for a fresh
one — and it covers them without the tool ever having to answer a question
ADR 0027 deliberately made unanswerable, which is whether an image is its own.

**Nothing is ever removed.** Not a sidecar for a video prdb has forgotten, not
an image, not a directory, not a file whose scene no longer matches anything. A
refresh writes over its own document or into an empty name, and those are the
only two things it does to a filesystem. A row whose directory no longer holds
the file it names is skipped and left alone: reconciling the library with the
table is a different decision, and guessing at it inside a run that nobody is
watching is how a tool deletes something.

**A rewrite is not an entry in the operation log.** An entry is shaped for the
way back — where the file was, where it went, how it moved, what it measured —
and none of that describes a document being replaced by a better version of
itself. The arithmetic decides it as firmly as the shape does: a nightly refresh
writing a few hundred entries would push the moves that *can* be undone out of a
twenty-thousand-entry log in weeks, and the trim drops whole runs, so it would
be dropping real history to keep a record of metadata being corrected. The run
row ADR 0032 gives it carries the account, the run's own report names what it
rewrote while it is on the screen, the container's log has each one, and
`MetadataCheckedAt` says when a scene was last looked at.

**A refresh cannot be undone, and the History says so rather than offering a
button.** `LoggedRun.CanBeUndone` is already *"the run is a filing"*, so
[ADR 0029](0029-undo-returns-one-operation-or-one-run-and-refuses-rather-than-guessing.md)'s
refusal covers this without being softened. It is not a gap being lived with:
everything a refresh replaced carried the tool's own marker, so nothing a person
wrote was lost; everything in the new document came from prdb, so the way back
is to ask prdb again, which is the run itself; and an image it wrote went where
there was nothing, where deleting the file is the affordance ADR 0027 gives. The
dangerous half of a bulk run is the half that moves files, and this run does not
move any.

**Undoing a filing still works on a scene a refresh has touched, with one thing
left behind.** The rewritten `movie.nfo` still carries the marker, so ADR 0029's
rule that nothing is removed on the strength of the log alone still passes and
the document leaves with the video. An image a *refresh* wrote is a different
matter: it is not on that filing's entry and has no fingerprint recorded there,
so the undo leaves it, and the scene directory is therefore not empty and is
left as well. That is the correct behaviour of two rules meeting — an undo
removes what its own operation put there and nothing else — and it is a
consequence rather than a bug to fix later.

**The media server is told after the run, and only about what changed.** Exactly
as `FilingRunner` does it: after the report is published, swallowing everything,
never in the path of a write, because ADR 0018 keeps a server that may be
switched off out of it. Only the scenes whose sidecar this run actually rewrote
are worth naming, which is what makes the comparison above pay for itself twice
— a run that changed nothing asks the media server for nothing.

## Alternatives considered

- **Trigger on `updatedAtUtc` and skip the comparison.** Rejected above on the
  cast entry, and rejected again on cost: it is a value that only arrives inside
  the answer, so it saves no request, and it would put a second copy of prdb's
  state in the database for filing to keep in step — the thing ADR 0017 draws
  its line at.
- **Only rewrite when the title changed.** Rejected. The date and the cast are
  the other two cases `VISION.md` names, and a document that differs in any
  field is one the media server reads differently.
- **Keep the replaced document so the run can be undone.** Rejected. It puts a
  copy of the library's metadata in SQLite, growing with the library rather than
  with what the tool did, and ADR 0028 chose flat columns and counted bounds
  against exactly that. What it would restore is a wrong title.
- **Write a `.bak` next to the sidecar instead.** Rejected for the reason
  ADR 0024 rejected a hidden marker file: two files to keep in step, one of
  which a user copying a directory takes and the other of which they do not —
  and here the second one is a stale copy of metadata sitting in a directory the
  media server reads.
- **Log every rewrite as an operation entry.** Rejected on the trim arithmetic
  above. The compromise of logging them and raising the bounds was rejected in
  the same breath ADR 0031 rejected it: the History screen is what somebody
  opens when something went wrong with their *files*.
- **Let a refresh replace an image when prdb's first image has changed.** This
  is the amendment ADR 0027 said would need its own reasoning, and the reasoning
  does not hold. The tool cannot tell its own `fanart.jpg` from the user's —
  0027 deliberately left no marker, on the grounds that a tool which never
  replaces does not need one — so "replace ours" is a question with no answer on
  disk. And prdb's documented order fixes an *order*, not a *ranking*: a
  different first image is not a better one. Deleting the file is still how a
  fresh image is asked for, and now the next refresh brings it rather than the
  next filing.
- **Follow `/videos/images/changes` to find scenes that have gained an image.**
  Rejected for now, and noted because it is new: the feed arrived in `Prdb.Sdk`
  0.8.0, later than the 0.6.2 this repository takes. The run already asks about
  the same videos for the sidecar's sake and `VideoDetailDto.Images` rides in on
  that answer, so the images are already paid for; the feed is global and would
  page changes for every site in prdb to answer a question about one user's few
  thousand scenes. It becomes interesting the day the sidecar half gets a feed
  of its own and the pass stops being needed.
- **Record the image a refresh wrote on the filing entry that is still standing,
  so an undo removes it.** Rejected. An entry is what a run did, and a log later
  runs edit stops being a record of anything. ADR 0028 adds the sidecar and the
  image to an entry after the move because that same operation wrote them; a
  different run writing into the same directory is not that.
- **Refresh only where the tool can see the media server**, so that nothing is
  rewritten that nothing will read. Rejected: the connection is optional
  (ADR 0018) and buys convenience rather than correctness, and a library whose
  sidecars are right is right whether or not a server has been told.

## Consequences

- The second refresh over an unchanged scene writes nothing, asks the media
  server for nothing and leaves the file's timestamp alone. In the steady state
  this feature is a few requests and a lot of reads, which is what makes it safe
  to leave running.
- A change to `MovieNfo` itself — a new element, a reworded notice — makes the
  next refresh rewrite every sidecar the tool has ever written, once, and tell
  the media server about all of them. That is correct rather than a bug: the new
  document is the better one. It is also a reason not to reword that file
  casually — and the first instance of it is this decision's own, because the
  notice in the document says the sidecar is rewritten "whenever the tool files
  this scene", which stops being the whole truth here.
- A hand-written `movie.nfo` survives a refresh exactly as it survives a filing,
  and the way to hand a file to the tool is still to delete the marker line —
  now with a run that acts on it without waiting for something to be filed into
  the scene.
- Turning artwork on after a library has been filed fills it in over the
  following runs. Before this decision the only way was to file into every scene
  again.
- Undoing a filing whose scene a refresh gave an image to leaves the image and
  the directory behind. It is legible — there is a picture in a directory with
  nothing else in it — and deleting it is the same one gesture as everywhere
  else in ADR 0027.
- Nothing a refresh did can be taken back by pressing something. The History
  says so on the row rather than offering a button that would have to guess, and
  the cost of that honesty is bounded by the fact that a refresh only ever
  writes over the tool's own work.
