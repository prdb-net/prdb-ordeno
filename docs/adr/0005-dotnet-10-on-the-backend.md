# 0005. .NET 10 on the backend

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

The tool is a long-running service in a container with a browser UI: it scans
watched directories on a timer, runs `ffmpeg` per file, keeps a review queue,
and has to stay up for months on hardware nobody logs into. The implementation
language was left open deliberately while the shape of the product was settled,
which left every instruction in the repository hedged: no build command, no test
command, no SDK choice, and a `.gitignore` that ignored nothing a build
produces.

Reaching the API is not what decides this. `prdb-sdk` generates clients for
Python, TypeScript, Go and C# from the same OpenAPI document — the same 49
operations with the same shapes, each with its own test for the rule that keeps
`X-Api-Key` on the API host. Measured against the API alone, the four are
interchangeable.

The hashes are where they stop being interchangeable. `Prdb.Hashing` exists in
C# and nowhere else: thirteen files, about 1,400 lines, and by
[ADR 0004](0004-hashes-stay-bit-compatible-with-stash.md) it reaches
bit-compatibility with Stash by reproducing that implementation's mistakes on
purpose. The method is written down normatively with test vectors as data, so a
port is possible and verifiable. It is still a second implementation of a
specification where a subtle deviation does not degrade the result but produces
values that match nothing — carried indefinitely, and re-verified every time the
method moves. Choosing any language other than C# is choosing to own that.

prdb itself is .NET 10, so the toolchain, the CI shape and the container base
are already in use next door.

## Decision

The backend is .NET 10, and prdb is reached through the `Prdb.Sdk` package.
.NET 10 is the current LTS; .NET 9 left support in May 2026 and .NET 8 leaves it
in November, so choosing .NET is choosing 10.

The frontend framework is deliberately still open, with one constraint that
follows from this decision rather than from taste — see the consequences.

## Alternatives considered

- **Go.** The smallest image of the four, a single binary, and a good fit for a
  watcher; Stash itself is Go. Rejected: the hasher would have to be ported and
  then maintained against a specification this repository does not own.
- **TypeScript.** The only option that would put one language on both sides of
  the application, which is worth something for a tool whose surface is an
  onboarding wizard and a review queue. Rejected: the same port, and the weaker
  side of the two for subprocess-heavy background work.
- **Python.** Rejected: the same port again, the weakest of the four for a
  long-running concurrent service, and the least help from the compiler on write
  paths that delete files.

None of the three was ruled out on the API, and all three remain reasonable
languages for this program. The decision rests on the hasher and on the
toolchain already being here.

This reopens if `Prdb.Hashing` ever reaches a second ecosystem, or if hashing is
split out into a service of its own. Then the argument above dissolves and the
question is genuinely open again.

## Consequences

- `Prdb.Sdk` follows without a second decision. It targets `net8.0` and is
  consumed from `net10.0` unchanged, and it covers the identify, submission and
  fulfilment endpoints.
- `Prdb.Hashing` is available as a package rather than something to port.
- `dotnet build` and `dotnet test` are the verification commands, against an SDK
  version pinned in `global.json`.
- Nullable reference types and warnings as errors are part of how the write
  paths are kept honest, not a style preference.
- **The frontend must build to static assets the backend serves.** A framework
  with a server-side runtime of its own would put a second runtime into an image
  that already carries `ffmpeg`, which is the cost this decision is otherwise
  careful about. That rules out a deployment mode, not a framework. Nothing is
  added until the framework is decided.
- The image carries a .NET runtime alongside `ffmpeg`. Worth watching, not worth
  reopening: this audience already runs several .NET containers next to this one
  — Sonarr, Radarr and Prowlarr are all .NET — so the runtime is the norm on
  their hardware rather than something this tool introduces.
- Memory is the number that matters on a NAS, not image size. The default server
  GC takes as much heap as it is allowed, which on a 2 GB box looks like a leak
  in the appliance's own dashboard whether or not it is one. Workstation GC and
  a heap limit taken from the container's cgroup are settled with the first
  container, not after the first report.
- `linux-arm64` from the first image. Synology and single-board hardware are the
  described audience, so the build is multi-arch from the start rather than
  retrofitted once someone asks.
- Trimming and NativeAOT are not promised as the answer to image size. The
  generated SDK serialises reflectively, so any claim there has to be measured
  before it is made.
