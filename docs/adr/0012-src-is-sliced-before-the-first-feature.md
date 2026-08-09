# 0012. `src/` is sliced before the first feature

- **Status:** Accepted
- **Date:** 2026-08-09
- **Supersedes:** the instruction in `AGENTS.md` that the division inside `src/`
  should follow from the first real feature rather than be laid out in advance

## Context

Until now `AGENTS.md` said to leave the internal division open and let the first
real feature shape it. That is good advice for most projects: boundaries drawn
before there is code to constrain are guesses, and guessed boundaries are the
ones that get worked around rather than respected.

This project has one property that outweighs it. `AGENTS.md` also carries a hard
rule — the tool moves, renames and deletes files a user cannot get back, every
write path must be able to say what it would do without doing it, and nothing is
written on a failed lookup. In a single project, an HTTP endpoint that calls
`File.Move` directly is one line away, and nothing but review stands between the
code and that line. The rule wants a boundary that the compiler enforces, and
that boundary is worth having before the first write path exists rather than
after.

## Decision

Four projects from the first commit, with the dependency direction fixed:

```
src/Prdb.Ordeno.Core            domain and application logic; no I/O
src/Prdb.Ordeno.Infrastructure  SQLite/EF Core, the filesystem, Prdb.Sdk, Prdb.Hashing
src/Prdb.Ordeno.Host            ASP.NET Core: HTTP, auth, static assets, workers, composition
src/Prdb.Ordeno.Frontend        React and Vite

tests/Prdb.Ordeno.Core.Tests
tests/Prdb.Ordeno.Infrastructure.Tests
```

`Core` references neither EF Core, nor `Prdb.Sdk`, nor the filesystem — it
declares what it needs as interfaces and `Infrastructure` implements them.
`Host` references both and wires them together; nothing references `Host`.

## Alternatives considered

- **One host project, split later** — the previous instruction. Rejected here,
  and only here: it is cheaper up front and it is what this repository told
  itself to do, but it leaves the dangerous paths reachable from everywhere by
  default. Deciding that a rule matters and then leaving its violation one line
  away is the kind of gap that is discovered in a bug report.
- **A separate `Prdb.Ordeno.Database` project** alongside `Infrastructure`.
  Rejected for now: the persistence and the filesystem are both adapters behind
  the same kind of interface, and splitting them costs a project boundary for a
  distinction nothing yet needs. It becomes reasonable if migrations grow their
  own tooling.
- **A namespace root without the `Prdb.` prefix.** Rejected: the tool consumes
  `Prdb.Sdk` and `Prdb.Hashing` and is published under the same label; a
  different root would suggest a separation that does not exist.

## Consequences

- A reference in the wrong direction fails the build instead of being noticed in
  review. That is the entire point of the decision, and it is worth keeping
  strictly: the first `// just this once` reference from `Core` to
  `Infrastructure` ends it.
- `Core` holds the rules that the tests care about — what a preview says, when a
  duplicate is skipped, what makes a file a candidate — and can test them
  without a filesystem. The tests that need a real filesystem and a real SQLite
  file live in `Infrastructure.Tests`, which is where `AGENTS.md` wants them:
  a half-finished cross-device copy is not a thing a mock can have.
- Cross-filesystem copy-verify-delete, the ffmpeg calls and the prdb requests
  all sit in `Infrastructure` behind interfaces `Core` drives, which is what
  makes "ask a write path what it would do" the same code path as doing it.
- The boundaries may still be wrong. Moving a type between projects is cheap
  while the code is small, so this gets revisited at the first feature that
  fights the layout — the failure to avoid is quietly bending the dependency
  rule to keep the diagram intact.
- A fifth project is a decision, not a habit. This layout is the floor, not a
  pattern to keep applying.
