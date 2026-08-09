# 0015. Tests may reference the host; `src/` may not

- **Status:** Accepted
- **Date:** 2026-08-09
- **Amends:** [ADR 0012](0012-src-is-sliced-before-the-first-feature.md)

## Context

ADR 0012 fixed the dependency direction with "nothing references `Host`", and an
architecture test enforced it across every project in the repository.

The first feature to need the wiring tested ran into it. What ADR 0010 promises
— that the setup path closes the moment a password exists, that everything else
is behind the cookie — is not a property of any class. It is a property of how
the application is composed: an authorisation fallback policy, and the handful
of endpoints that opt out of it. A test that recreates that composition proves
its copy is right and says nothing about the application, which is worse than no
test because it reads like one.

Checking it for real means hosting the application as `Program.cs` builds it,
and that means a test project referencing `Prdb.Ordeno.Host`.

## Decision

The rule is about `src/`. No project under `src/` references
`Prdb.Ordeno.Host` — it is the composition root, and a library that depends on
it stops it being one. A test project may.

`ArchitectureTests` enforces the rule as scoped here.

## Alternatives considered

- **Keep the rule absolute and test the wiring by other means.** Rejected: the
  only other means is asserting against a second composition written in the test,
  which cannot catch the mistake that matters — an endpoint added later without
  the opt-out it needs, or one that opts out when it should not.
- **Move the endpoints into `Infrastructure` so a library test can reach them.**
  Rejected, and worse than it sounds: it would put HTTP into the layer that
  exists to keep I/O behind interfaces, in order to satisfy a rule about
  layering.
- **Edit ADR 0012.** Rejected because `AGENTS.md` says decisions are superseded
  or amended by new ones rather than rewritten. The original reasoning still
  holds; only its reach was wider than intended.

## Consequences

- `tests/Prdb.Ordeno.Host.Tests` exists and hosts the real application with
  `WebApplicationFactory`, pointed at a data directory of its own.
- Nothing in it may replace a service with a double for convenience. The wiring
  is the subject; a test that stubs its way past the wiring has stopped testing
  what this ADR exists for.
- `Program` carries a `public partial class Program` declaration so the factory
  can name it. That is the cost of hosting the real entry point, and it is
  cheaper than the alternatives above.
- The dependency direction inside `src/` is untouched, and still checked.
