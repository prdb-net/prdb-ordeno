# 0032. A refresh is a run of its own, over what the tool filed, and it moves nothing

- **Status:** Accepted
- **Date:** 2026-08-19

## Context

[ADR 0024](0024-the-sidecar-is-written-by-filing-and-only-over-its-own.md)
settled that a sidecar is written as part of filing a video and at no other
time, and deliberately left *when an existing one is refreshed* open — its own
consequences name the gap: *"A scene filed before a correction keeps the old
sidecar until something files into that scene again."*
[ADR 0027](0027-artwork-is-one-image-written-only-where-there-is-none.md)
inherited the same gap for artwork. `VISION.md` names the case both are about:
prdb corrects a title, a date or a cast entry, and the file written last spring
still says the old thing.

[#38](https://github.com/prdb-net/prdb-ordeno/issues/38) is that gap, and it was
made to wait for the operation log and its undo
([#19](https://github.com/prdb-net/prdb-ordeno/issues/19)) because a refresh is
an unattended run that rewrites files. The log exists now
([ADR 0028](0028-the-operation-log-records-what-changed-and-is-trimmed-by-whole-runs.md),
[ADR 0029](0029-undo-returns-one-operation-or-one-run-and-refuses-rather-than-guessing.md)),
and so does the first timer that writes without anybody watching
([ADR 0031](0031-the-filing-timer-is-off-until-it-is-turned-on.md)).

**The obvious answer is the one two decisions exist to prevent.** Sweeping the
library on a timer and re-asking prdb about everything is exactly what
[ADR 0017](0017-prdbs-answer-is-stored-and-asked-for-once.md) forbids for files
— a file is asked about once, and only its bytes changing or a perceptual hash
arriving makes it worth asking again — and what
[ADR 0001](0001-identification-runs-in-prdb.md) exists to design against:
spending a rate-limited quota to be told the same thing.

**And prdb does not offer the cheap way out.** It publishes seek-paged delta
feeds — `/actors/changes`, `/videos/filehashes/changes`,
`/wanted-videos/changes`, `/favorite-actors/changes`, `/favorite-sites/changes`,
`/indexer-filehashes/changes`, `/video-user-images/changes` — and there is no
`/videos/changes`. `GET /videos` filters by `createdAfter` and `createdBefore`
and sorts by title, release date or creation timestamp, so a video whose title
was corrected this morning is indistinguishable from one nothing has touched
since it was created. `VideoDetailDto` does carry `updatedAtUtc`, but only
inside an answer — which makes a changed video cheap to spot in *writes* and
never in *requests*.

What is left is `POST /videos/batch`: fifty ids per request, ids prdb does not
know silently omitted. It is the same request filing already makes for the same
reason, and it fixes the cost of a pass over a library at **one request per
fifty filed scenes**. A thousand-scene library is twenty requests. That number
is what makes this decision possible at all, and it is the number every bound
below is derived from.

The other half of the cost is the one identification does not have to think
about. Every metered answer reports two windows, an hour and a month
(`RateLimitWindow`: limit, remaining, seconds until one slot frees). A run that
files spends against the hour in bursts; a run that repeats nightly over a whole
library is a *monthly* consumer, and it is the first thing in this tool that is.

## Decision

**A refresh is a run of its own.** Not a phase of filing, not something a scan
does: one entry point, one gate, one row in the log, in the same shape ADR 0022
and ADR 0031 gave filing. Filing keeps ADR 0024's rule unchanged — it writes a
sidecar for what it files and nothing else — and everything about a scene that
is already in the library happens here.

**It is asked for by a person, and there is a timer that is off until somebody
turns it on.** The button is always there; the switch is only about the timer,
next to `UnattendedFiling` and the artwork one in `/settings/library`
([ADR 0026](0026-an-area-may-have-sections-and-the-settings-do.md)), false for a
fresh installation and false for one upgraded into the release that adds it, and
not an onboarding step. This is the second unattended write path in the tool and
it gets the first one's rule verbatim, for the reason `AGENTS.md` gives: a tool
that starts rewriting files because it was upgraded is the surprise the opt-in
rule exists to prevent.

**Its subject is what the tool filed, not what is in the library.** The rows in
`FiledVideos` whose `LibraryRoot` is the library the tool is pointed at now, one
scene directory at a time — the same rows filing reads to tell a second quality
from a second scene. The tool does not walk the library root looking for
documents to own: a directory it did not file is somebody's own, a row that was
deleted was deleted on purpose, and a table saying exactly which scenes are the
tool's own is better evidence than a filesystem that cannot say whose a
directory is.

**It moves nothing, and it renames nothing.** A corrected title changes the name
the layout would produce, and the refresh does not act on that: the video keeps
its file name, the scene keeps its directory, and only what is *inside* the
directory is brought up to date. Re-filing an existing library under a changed
name or a changed layout was excluded from #18 deliberately and stays excluded;
it is the other half of what `VISION.md` calls "the library is not written
once", it is a bulk *move* over files a user considers sorted, and it needs its
own decision rather than arriving as a side effect of a metadata refresh. What
this run may write is
[ADR 0033](0033-a-refresh-rewrites-only-its-own-and-cannot-be-undone.md)'s
question, and the answer there is narrower still.

**The scenes least recently checked go first.** Each scene directory carries
when it was last looked at (`MetadataCheckedAt` on `FiledVideo`, null for every
row that exists before this ships, which puts the whole library first in line
once). That single column is what makes a run resumable without a cursor table:
a run that stops halfway has stamped what it reached, and the next one starts
where it stopped rather than at the top of the library forever.

**A run stops on the quota and says so, and that is not a failure.** It paces
off the readings it is already getting, exactly as identification does, and it
stops far earlier: `RefreshSchedule.QuotaReserve` is an order of magnitude above
`IdentificationSchedule.QuotaReserve`'s five, because identification is the loop
— a download nobody has identified cannot be filed at all — and a refresh is the
tool tidying up after itself. It yields on the monthly window as well as the
hourly one. A run that stopped early is a run that got part of the way through
the library, wrote everything it did reach, and will carry on tomorrow.

**The unattended run takes a slice; the asked-for one takes the library.**
`RefreshSchedule.Slice` — five hundred scenes, ten prdb requests — is what a
tick costs whatever size the library is, and `RefreshSchedule.Interval` is a
day. That makes the nightly cost of this feature a constant rather than
something that grows with what the user has filed, which is the property a
monthly quota actually needs. A five-thousand-scene library comes round every
ten days; a correction nobody is waiting for arrives within ten days, and
somebody who *is* waiting presses the button, which walks the whole library and
is bounded only by the quota. Both are the same run with a different bound, in
the shape ADR 0031 insisted on: not a second path.

**No preview.** ADR 0022's preview stands between a user and a move that loses a
file, and this run moves nothing. Producing one would also cost the entire
request budget of the run itself, because what would change is only knowable by
asking prdb — which is precisely what ADR 0024 refused to spend on a sentence
somebody may not act on. The run reports what it did, and it is in the History
afterwards.

**One gate over the library** (`LibraryGate`), the same one filing and undo
take. A refresh writing into a scene directory while an undo is taking that
scene apart is two plans over one directory, which is the thing the gate exists
for. It also means a refresh in progress is what a person pressing "file" is
told about, and the other way round.

**One row in the log per run, and an unattended run that changed nothing leaves
none.** `RunKind` gains a third value and the run row carries an account like
every other. The rule ADR 0031 wrote for the filing timer applies unchanged and
for the same arithmetic: a daily row saying nothing happened would spend a third
of a thousand-run log every year on the tool doing nothing, and the trim would
then be dropping real history to keep it. A run somebody asked for keeps its row
whatever it did. What the run may write into the log *below* the run row is
ADR 0033's question.

**It asks nothing about files.** ADR 0017 deferred "re-ask on a long timer" to
"the metadata refresh decision", and this decision sends it on rather than
answering it. It is a different question — about files in the download
directories rather than scenes in the library, priced per two hundred files
rather than per fifty scenes, and mostly answered "still unknown" — and prdb
already publishes the feed that makes it cheap: `/videos/filehashes/changes`
carries every filehash row that arrives, which is exactly the event that turns
one of the user's unidentified files into an identifiable one. Deciding that
here would bolt a second, differently-shaped run onto this one.

## Alternatives considered

- **Refresh the scenes a filing run passes anyway.** Rejected in ADR 0024
  already, and rejected again here for the sentence it used: *"a run that
  reports having moved nothing must not have been writing"*. It also refreshes
  exactly the wrong scenes — the ones being filed into today, rather than the
  ones filed a year ago that the correction is about.
- **Ask only about videos whose `updatedAtUtc` is newer than what we stored.**
  Rejected, and this is the one that looks free. The timestamp only arrives
  *inside* the answer, so it cannot decide whether to ask; it could only decide
  whether to write, which
  [ADR 0033](0033-a-refresh-rewrites-only-its-own-and-cannot-be-undone.md)
  answers better and without a stored copy to keep in step.
- **Poll `GET /videos?sortBy=createdAtUtc` and watch for changes.** Rejected: it
  is a creation timestamp. It finds videos prdb has added, which is a question
  about the review queue and not about a scene already filed, and it would page
  a global corpus to answer something about one user's few thousand scenes.
- **Ask prdb for a `/videos/changes` feed and wait for it.** Not rejected — it
  is the feed that would make this a delta rather than a pass, and it is worth
  asking for upstream. Rejected as a *prerequisite*: the gap is real today, the
  pass costs one request per fifty scenes, and a decision that waits for another
  project's roadmap is a decision that leaves last spring's title in the library
  indefinitely.
- **A timer with no button.** Rejected. Somebody who has just corrected a title
  in prdb is the person most likely to want this, and telling them to wait until
  tonight for a run that is off by default is telling them the feature does not
  exist.
- **A button with no timer.** Tempting, and it is what ADR 0022 did for filing a
  release ago. Rejected on the same ground ADR 0031 overturned it: `VISION.md`
  puts the value in the unattended running, the information the consent depends
  on now exists — a History screen and a way back — and a correction that only
  lands when somebody remembers to press something is a correction that does not
  land.
- **Let the user choose the interval.** Rejected, as `ScanSchedule`'s and
  `FilingSchedule`'s were: it is a field to validate, store, explain and get
  wrong, in exchange for a number nobody can currently choose better than the
  tool can. The switch is the setting.
- **Sweep the library root for `movie.nfo` files carrying the marker**, instead
  of reading `FiledVideos`. Rejected. It walks a NAS and opens every document to
  answer a question a local table already answers, and it would adopt scenes
  whose row was deliberately deleted — including, after an undo, a directory the
  tool no longer holds anything in.
- **Refresh everything on every run and rely on the writes being idempotent.**
  Rejected: the requests are not idempotent in cost. A five-thousand-scene
  library is a hundred requests a night against a monthly window that
  identification also draws on, to discover that nothing has changed on all but
  a handful of scenes.

## Consequences

- A title, a date or a cast entry corrected in prdb reaches the library on its
  own, which is the case `VISION.md` names and the reason the sidecar is worth
  writing at all.
- The feature costs one prdb request per fifty scenes and, with the timer on, a
  fixed ten requests a day whatever the library holds. A library big enough for
  that to matter is refreshed a slice at a time and comes round on a schedule
  the user can compute.
- A scene filed under last spring's title keeps the directory and the file name
  it was filed with, and the sidecar inside it says the corrected thing. That
  divergence is deliberate and it is what the media server reads; closing it is
  a re-filing decision that does not exist yet.
- The tool now has two switches that let it write while nobody is watching, and
  they are independent: filing moves files somebody downloaded, refreshing
  rewrites files it wrote itself, and somebody may reasonably want one without
  the other.
- The `FiledVideos` row gains a column that filing does not read, which is a
  small departure from what that table was for. It is the right place anyway:
  the row says what is true of the library now, and "when did anybody last check
  this" is exactly that kind of fact — it goes with the row when an undo takes
  the file back.
- A first refresh over a library filed before this shipped touches every scene
  in it, because every row starts with nothing stamped on it. It is bounded by
  the slice and the quota like any other run, and what it may actually write is
  bounded far harder by ADR 0033.
