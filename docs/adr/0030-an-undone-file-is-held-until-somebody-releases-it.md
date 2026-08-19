# 0030. A file that was put back is held until somebody releases it

- **Status:** Accepted
- **Date:** 2026-08-19

## Context

[ADR 0029](0029-undo-returns-one-operation-or-one-run-and-refuses-rather-than-guessing.md)
chose the smallest reversal that works. The video goes back to the download
directory it came from; the row that said where it was, prdb's answer about it
and any decision a person made about it do not come back, because they were
deleted when it was filed. The ordinary loop takes it from there — the next scan
finds it, prdb is asked once more, and it is a candidate again.

That is the right reversal and it has a consequence the ADR could name but not
act on: **by the morning an undone file is indistinguishable from a fresh
download.** Same directory, same bytes, and nothing anywhere saying anything ever
happened to it. prdb answers what it answered yesterday — the bytes have not
changed, and [ADR 0017](0017-prdbs-answer-is-stored-and-asked-for-once.md) is
built on that being the same answer — so the plan that files it is the plan the
user rejected, computed again and this time carried out by nobody.

Today nothing refiles it, because nothing files anything unasked
([ADR 0022](0022-filing-happens-when-it-is-asked-for.md)). That is the only
reason, and it is the reason
[#43](https://github.com/prdb-net/prdb-ordeno/issues/43) is about to remove. *An
unattended run that refiles overnight what somebody undid that evening is the
way back cancelled by the feature it unblocked.*

Why somebody undid it is not knowable here, and the possibilities do not point
in one direction: the layout was wrong, the file was recognised confidently as
the wrong scene — which the review queue does not cover, because that queue is
for what prdb *could not* settle
([ADR 0023](0023-a-persons-answer-is-kept-beside-prdbs.md)) — or the scene is one
they did not want in the library at all. What they share is the only thing that
matters here: the answer the tool would work out again is the answer that was
rejected.

## Decision

**An undo leaves a hold on the file it put back.** One row per returned file,
written by the undo: the path the file went back to, when it had been filed and
what it had been filed as, and when it came back. It is written first among the
writes that follow the move, before the entry is marked undone — a file that is
back with no hold on it is the state this decision exists to prevent, so the
interruption that can produce it is the one to design against.

A relabel leaves none. Nothing came back to a download directory, so there is
nothing to hold.

**A held file is not filed by any run.** Not by the timer, and not by the button
either. Two reasons, and the second is the load-bearing one:

- The preview is produced by the code that performs the run, and it is the whole
  shape ADR 0022 turns on. A hold that only the unattended run respected would
  make the plan depend on who was asking — one behaviour a person reads on a
  screen, another that runs at three in the morning, and the dangerous one is
  the one nobody reads.
- Somebody who undid two hundred files last night and files fourteen new
  downloads this morning presses one button. If that button also refiles the two
  hundred, the way back was cancelled by a person who never intended it —
  "a button is easier to press than a command is to type", which is the sentence
  in `AGENTS.md` this rule follows from.

**The hold is on the plan, on the screen, and lifted by an act of its own.** It
is a filing outcome like the others: the row says the file was put back, when,
and what it had been filed as, and next to it is a release — for that file, or
for every held file at once. Releasing moves nothing. It makes the file ordinary
again, and the ordinary path takes it from there: the preview, then the button
or the timer. So the way out of a hold is one deliberate click rather than two
hundred, and it is still not the click that moves files.

**The hold is keyed to the path, and it goes when the file does.** There is
nothing else to hang it on — the row that identified the file was deleted when
it was filed and ADR 0029 does not put it back — and the path is what identifies
a file in a download directory everywhere else in this tool. It is dropped in
three ways, and only these:

- Somebody released it.
- The bytes at that path changed. The same rule, in the same statement, that
  forgets prdb's answer and a person's decision (ADR 0023): a different file at
  that name is not the file somebody put back, and holding it would be holding a
  download nobody has seen yet.
- A scan that walked its directory did not find the file. It is gone from the
  download directories, and so is the tool's memory of it — the same rule the
  discovered rows follow. A directory that could not be read keeps its holds,
  for the reason it keeps its rows: "I could not look" and "there is nothing
  there" are not the same answer.

**A hold is not a dismissal, and not a decision about the video.** ADR 0023's
dismissal says "this is not to be filed, ever, until I say otherwise" and is set
in the review queue, which a recognised file never reaches. A hold says what
happened — this file was filed and you took it back — and it is about one file
at one path. Nothing about it is contributed anywhere, nothing about it outranks
prdb, and it does not survive the bytes it was written about.

**prdb is still asked about a held file.** The hold decides what is done with
the answer, not whether it is fetched. The file is in a download directory like
any other, it is on the Downloads screen like any other, and a tool that
pretended not to see it would have a screen that does not add up.

## Alternatives considered

- **A grace period** — refuse to refile anything undone in the last day, week,
  month. Rejected. It reads as arbitrary the first time it is wrong, and it is
  wrong for exactly the case it would be built for: somebody undoes a run on
  Tuesday evening and gets to the layout that caused it at the weekend. A rule
  whose failure mode is "the tool did the thing you took back, because you were
  slow" is not a rule.
- **Only the unattended run respects the hold**; a person pressing the button is
  the "somebody says otherwise". Rejected for the two reasons under the decision
  above. It is the smaller change and the more attractive one — no release
  action, no new outcome on the screen — and it buys that by making the preview
  a different thing from the run.
- **Restore the identification and the decision from the log**, and mark the
  file there. Rejected by ADR 0029 on its own terms, and here it would put the
  hold on a row the scan replaces whole every time prdb is asked again — which
  is precisely the mistake ADR 0023 exists to avoid.
- **Leave an undone file out of the loop entirely**: do not scan it, do not ask
  about it. Rejected. It is a file in a download directory, the Downloads screen
  counts it, and a tool that hides a file it has decided something about is a
  tool whose screens have to be believed rather than read.
- **Hold the scene rather than the file.** Rejected. An undo is about files that
  moved: somebody who takes back a 2160p copy has said nothing about the 1080p
  file still waiting in another directory, and a hold on the scene would answer
  a question nobody asked.
- **Mark the file on disk** — a dotted name, an extended attribute. Rejected
  outright. The download directories are read-only to this tool, and that rule
  is worth more than the convenience of a marker that travels with the file.

## Consequences

- The Filing screen gains a state that is neither "would be filed" nor "cannot
  be filed": held, with the date it came back and what it had been filed as.
  That is what somebody who undid a run last week reads when they wonder why
  nothing is happening to it.
- Undoing a run of two hundred leaves two hundred holds and one button that
  lifts them. Filing them again is then the ordinary path, preview included.
- The tool keeps a row about a file it has otherwise forgotten. It is bounded by
  the download directories rather than by the log: when the file goes, the hold
  goes, and it is a path and three timestamps in the meantime.
- An undone file still costs one prdb request, exactly as ADR 0029 said it
  would. Nothing here changes that, and nothing here is allowed to grow into a
  second store of what prdb answered.
- The timer becomes safe to build, which is
  [ADR 0031](0031-the-filing-timer-is-off-until-it-is-turned-on.md).
