# 0022. Filing happens when it is asked for, until there is a way back

- **Status:** Accepted
- **Date:** 2026-08-15

## Context

Everything the tool does so far it does on a timer: it walks the download
directories ([ADR 0016](0016-directories-are-scanned-on-a-timer.md)), asks prdb
what it found, and hashes in the background. None of that touches a file the
user owns. Filing is the first thing that does, and the obvious next move is a
fourth timer.

`VISION.md` puts two sentences next to each other that decide this. "The value
is in the unattended running" — and, under the principles, "before anything
moves, the user must be able to see exactly what would happen". The second is
not a qualification of the first; it is what makes the first safe. The same
document is blunt about the missing half: *"Without a way back, the only safe
way to use such a tool is to not let it run unattended, which is the whole point
of it."*

The way back is the operation log and its undo, and that is
[#19](https://github.com/prdb-net/prdb-ordeno/issues/19) — deliberately a
milestone of its own, and not built. So the question is not whether filing ever
runs unattended. It is what happens in the releases between now and the log.

## Decision

**In this version nothing is filed until somebody asks for it, having been shown
what would happen.** The Downloads screen states the plan — every file, where it
would go, what would be relabelled, what would be skipped and why — and a button
carries it out. No timer files anything.

**The timer arrives with the operation log**, not before, and it is the same
call behind the same button.

What makes that a schedule rather than a rewrite is the shape underneath, which
the hard rule in `AGENTS.md` requires anyway: **the preview is produced by the
code that performs the run.** Planning writes nothing, and the run is that plan
carried out — one path, not a description of one. Turning it into an unattended
run is giving the same call a timer instead of a button, and the plan it would
have shown becomes the thing the log records.

The plan is computed fresh at the moment of the run, not carried over from the
preview. A directory can be occupied in the seconds between them, and acting on
a plan that has gone stale is exactly the failure the preview exists to prevent.
What the user confirms is the intention; what runs is the same computation,
performed again.

## Alternatives considered

- **A filing timer now, like the other three.** Fulfils "set up once and left
  alone" a release earlier. Rejected: an unattended run that files two hundred
  files overnight under a rule that turns out to be wrong is the exact scenario
  `VISION.md` calls the reason undo exists, and it would be shipped before the
  undo. The first release this tool makes must not be the one where somebody
  learns that.
- **A timer with a switch, off by default.** Reasonable-looking, and it fits the
  "destructive operations are opt-in" line. Rejected as a way of moving the
  decision onto the user without giving them what it needs: the switch would be
  offered next to a screen that cannot yet show what was filed last night or put
  any of it back. A setting is not consent when the information the consent
  depends on does not exist yet.
- **File automatically, but only the unambiguous cases.** Rejected because
  "unambiguous" is a property of what prdb answered, not of the move, and the
  moves that go wrong go wrong on the filesystem — a share that vanished, a
  target that filled up — where the confidence of the identification buys
  nothing.

## Consequences

- The onboarding sentence changes rather than disappearing. "Nothing is filed
  yet" becomes "nothing is filed unless you ask", and the sentence that says the
  tool watches on its own now has a limit stated next to it.
- The preview is a first-class read path with its own endpoint, not a debug
  view: it is what somebody reads before pressing the button, and later what the
  log is written from.
- A user who wants the unattended behaviour today does not have it. That is the
  honest position for a tool whose one hard rule is about files nobody can get
  back, and it is one release of it.
- #19 gets smaller: the log lands against a filing path that already produces,
  before it moves anything, exactly the description the log needs.
