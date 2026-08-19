# 0031. The filing timer is off until it is turned on, and files exactly what the button files

- **Status:** Accepted
- **Date:** 2026-08-19

## Context

[ADR 0022](0022-filing-happens-when-it-is-asked-for.md) did not reject the filing
timer. It made it wait for one thing — *"the unattended run arrives with the
operation log and its undo and not one release earlier"* — and that log exists
now ([#19](https://github.com/prdb-net/prdb-ordeno/issues/19),
[ADR 0028](0028-the-operation-log-records-what-changed-and-is-trimmed-by-whole-runs.md)
and
[ADR 0029](0029-undo-returns-one-operation-or-one-run-and-refuses-rather-than-guessing.md)).
The one question it handed on has been answered in
[ADR 0030](0030-an-undone-file-is-held-until-somebody-releases-it.md): a file
somebody put back is held, and no run files it until somebody releases it.

What is left is the timer itself, and `VISION.md` is plain about why it is worth
having: *"the value is in the unattended running"*. A tool that files what it
recognises only while somebody is looking at it is a tool somebody has to look
at.

## Decision

**The timer is `FilingRunner.TryFile` on an interval, and nothing else about
filing changes.** The same plan, computed by the same code, carried out through
the same gate, written into the same log, with the same way back. ADR 0022 built
filing so that this would be a schedule rather than a rewrite, and this is the
schedule.

**Off unless somebody turned it on.** A stored switch, false for a fresh
installation and false for one that is upgraded into the release that adds it: a
tool that starts moving files because it was upgraded is the surprise that the
opt-in rule in `AGENTS.md` exists to prevent. It lives with the library
settings, next to the artwork switch ([ADR 0026](0026-an-area-may-have-sections-and-the-settings-do.md)'s
sections), and it is **not** an onboarding step — the tool works without it, and
onboarding is what must be answered before anything happens at all.

ADR 0022 rejected exactly this switch a release ago, and the reason it gave is
the reason it is offered now: *"a setting is not consent when the information the
consent depends on does not exist yet"*. The information now exists. Somebody
turning this on has a History screen that shows what a run does, file by file,
and a button that puts one back.

**The interval is a constant, not a setting.** Fifteen minutes, in `Core` next to
the scan's, with the reasoning beside it — and the reasoning is the same as
`ScanSchedule`'s: a setting is worth adding once somebody has a reason to change
it, and until then it is one more thing in the UI to be wrong about. A shorter
interval buys nothing, because a file is not a candidate until two scans have
seen it unchanged ([ADR 0016](0016-directories-are-scanned-on-a-timer.md)); a
longer one is a preference nobody has expressed yet. The switch is the setting.

**A run nobody asked for starts only when something is waiting.** Whether there
is anything to file is a query over the tool's own tables; working out *what*
would happen reads the header of every waiting video, which is real work on
somebody's NAS. Doing that every fifteen minutes to be told that nothing has
arrived is the kind of background noise that makes people uninstall a tool.

**An unattended run that moved nothing leaves no row in the operation log.** This
amends ADR 0028, which keeps the row for a run that moved nothing on the grounds
that *"it is the answer to the question somebody who was asleep asks first"*.
That holds for a run somebody asked for. Nobody asked for this one, and a row
every fifteen minutes would push three years of nights out of a thousand-run log
in ten days — the trim would then be dropping real history to keep a record of
the tool doing nothing. A run that moved something is logged exactly as an
asked-for one is, account and all.

**Every run says who asked for it** — a person or the timer — which is the column
ADR 0028 left for whoever built this. It is on the run row and it is on the
screen: "you filed these" and "the tool filed these while nobody was watching"
are different sentences, and the second one is the one somebody reading the
History in the morning needs.

**What is on the screen stays true, and does not get vaguer.** The Filing screen
today promises that the tool files nothing on its own. That sentence goes, and
what replaces it depends on the switch rather than hedging: with the timer off,
filing happens when you ask for it and the switch is where to change that; with
it on, the tool files what it recognises every quarter of an hour, and every run
is in the History where one that went wrong can be put back. The same applies to
the sentence onboarding ends on.

**Unattended is not unsupervised.** Nothing degrades because nobody is watching,
which is already the hard rule in `AGENTS.md` and is worth restating where the
first unattended write path lands: a failed or partial lookup writes nothing, a
library that cannot be read files nothing and says so, and every move is
copy-verify-delete where it crosses a filesystem. The user reads the result
later, and the result has to be worth reading.

## Alternatives considered

- **A nightly run at a fixed hour.** Attractive — "it files overnight" is what
  people picture. Rejected: the tool does not know the user's timezone and does
  not collect one, so "three in the morning" is three in the morning somewhere
  else; and a download that finished at nine in the evening would sit unfiled
  for six hours for no reason. A quarter-hourly trickle is what makes the tool
  invisible, which is the point.
- **An interval setting**, with the switch. Rejected for the reason above. It is
  a field to validate, store, explain and get wrong, in exchange for a number
  nobody can currently choose better than the tool can.
- **Offer the switch during onboarding.** Rejected. Onboarding is the set of
  answers without which nothing happens at all; this is a behaviour switch on a
  tool that works without it, exactly like artwork. And ADR 0022's objection
  still bites at that moment: somebody who has not yet seen a single run has not
  got the information the consent depends on.
- **Let the timer file only what a person has previewed and approved.** Rejected.
  It turns the preview into a queue that goes stale — the exact failure ADR 0022
  computes the plan fresh to avoid — and it is not unattended running, it is
  deferred clicking.
- **Run the timer only when the last scan found something new.** Rejected in
  favour of asking what is waiting. A file becomes fileable for reasons that have
  nothing to do with the scan: somebody resolved it in the review queue, a
  perceptual hash arrived and prdb was asked again, a hold was released. The
  candidate query is the honest question and it is one query.
- **Keep the log row for every unattended run** and raise the trim's bounds
  instead. Rejected: it makes the History screen mostly a record of nothing
  happening, which is the page somebody opens when something did.

## Consequences

- The loop `VISION.md` describes is closed. Set the tool up once, turn this on,
  and downloads are filed without anybody pressing anything.
- The first write path in this tool that no person is standing in front of.
  Everything it does is in the History with the account it would have shown on
  the screen, and every run can be put back as a run.
- The Filing screen may show a run nobody started, including one that is under
  way — which is also what a person pressing the button while the timer is
  working is told, since both go through one gate.
- A quarter-hourly tick that finds nothing waiting costs one query and leaves no
  trace anywhere. That is deliberate: the log is a record of what the tool did to
  somebody's files.
- Notifications are now the thing this makes worth wanting, and they are
  deliberately not here (`VISION.md`, and the scope of #43): this decides when
  the tool acts, and that decides how it tells somebody it did.
