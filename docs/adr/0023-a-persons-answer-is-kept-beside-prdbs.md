# 0023. A person's answer is kept beside prdb's, and outranks it

- **Status:** Accepted
- **Date:** 2026-08-15

## Context

[ADR 0017](0017-prdbs-answer-is-stored-and-asked-for-once.md) settles what the
tool keeps of prdb's answer: one row per file, replaced whole by the next answer,
cleared when the bytes change. [ADR 0019](0019-a-missing-date-drops-the-segment.md)
settles what happens to the files that answer cannot name — nothing matched,
several videos matched equally well, or only the site was readable. They are not
filed, and they wait for a person.

That waiting is the review queue
([#16](https://github.com/prdb-net/prdb-ordeno/issues/16)), and building it asks
three questions the earlier decisions do not answer.

**Where does a person's answer live?** The obvious place is the identification
row: it already carries a video id, a title, a date and a site, and filing
already reads it. Writing the person's choice into those columns would need no
new table and no change to the filing path.

**What happens when the tool asks prdb again?** A file is asked about again when
its bytes change or when its perceptual hash arrives (ADR 0017). If the person's
choice sits in the identification row, the next answer replaces it — the row is
replaced whole, which is the property that keeps prdb's answer honest.

**Does saying "no" mean anything?** Some of what waits in the queue is not a
scene at all: a trailer, a sample, a file somebody does not want in their
library. Left alone it is offered again after every scan, forever.

## Decision

A person's decision about a file is stored in a table of its own, one row per
file, and it is **not** an identification.

It says what it is: who decided (a person), when, what they decided, and how they
got there — one of the candidates prdb named, or a video they searched for. That
last part costs one column and is what an assignment contributed back to prdb
would later rest on.

Two kinds of decision, and both are answers:

- **Assigned.** The person named the video. From then on the file is filed as
  that video, exactly as though prdb had named it.
- **Dismissed.** The person said this file is not to be filed. It is not deleted
  and it is not hidden from the inventory — it is a file the tool has been told
  to leave alone, and it stops being offered.

**A person's answer outranks prdb's.** Where both exist, filing reads the
person's. Re-identification writes only the identification row and cannot touch
this one, so an answer that arrives later — a corpus that has grown, a perceptual
hash that finally matched — does not quietly undo somebody's decision.

**Except when the bytes change.** The scan already forgets what a changed file
was; it forgets the decision about it too, in the same statement. The row is
keyed to a path, and a path whose contents have changed is a different video: the
alternative is a decision about last week's file naming this week's, which is how
the wrong scene ends up in the library under a name that looks deliberate.

**The tool asks prdb what the chosen video is; the browser only names it.** A
resolution arrives as a video id and nothing else. The title, site and date on
the row are fetched here, from prdb, because they become a directory name and a
file name — and a title that came from the browser is a path built from
unvalidated input.

**Resolving does not move anything.** It is an answer, not an instruction:
filing happens when somebody asks for it
([ADR 0022](0022-filing-happens-when-it-is-asked-for.md)), and a resolved file
joins the plan the next time it is worked out. The screen says so where the
resolving happens.

## Alternatives considered

- **Write the person's choice into the identification row.** Rejected. It is the
  cheapest change and it breaks the one property both rows depend on: an
  identification is replaced whole by the next answer, so a decision stored there
  survives only until the file is asked about again. Keeping a "do not overwrite
  this" flag on a row that exists to be overwritten is the same design with a
  place for the bug to hide.
- **Keep dismissal as a flag on the discovered file.** Rejected for the reason
  ADR 0017 gives for not putting the identification there: that row is an
  observation of what is on disk, and this is a decision somebody made. It would
  also be lost on the day the two are separated for any other reason.
- **Let a dismissal delete the file's row.** Rejected outright. The queue's
  answer to "not a video" must not be a way to make files disappear from the
  inventory — the tool would then rediscover it on the next scan and offer it
  again, and the version of this that does not is the one that deletes something.
- **Trust the title the browser sends with the resolution.** Rejected. It saves
  one request and turns a signed-in user's page into the thing that composes a
  path. The id is an identifier; everything that becomes a name is fetched.
- **File a resolution immediately.** Rejected: ADR 0022 is not a rule about
  timers, it is a rule about somebody having read what would happen. A resolution
  answers what a file *is*, and the plan is still what says where it goes.

## Consequences

- Filing has two sources of truth about what a file is, in a fixed order: the
  person's answer, then prdb's. Nothing else may be added to that list without
  another decision.
- The queue can show what somebody decided and when, separately from what prdb
  said, and undoing a decision is deleting one row.
- A dismissed file stays in the inventory and stays out of the queue and out of
  filing. It is counted, so a library that is half dismissed says so rather than
  looking half empty.
- A file whose bytes change loses its decision along with its identification, and
  comes back to the queue. That is the intended trade: the queue asks twice about
  a file somebody re-downloaded, rather than filing it under the old answer.
