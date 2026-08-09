# 0005. .NET 10 on the backend

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

The tool is a long-running service in a container with a browser UI. The
implementation language was left open deliberately while the shape of the
product was settled, which left every instruction in the repository hedged: no
build command, no test command, no SDK choice, and a `.gitignore` that ignored
nothing a build produces.

`prdb-sdk` publishes generated clients for Python, TypeScript, Go and C#, so the
API was reachable from any of them. Choosing the language and choosing the SDK
were one decision.

## Decision

The backend is .NET 10, and prdb is reached through the `Prdb.Sdk` package.

The frontend framework is deliberately still open.

## Alternatives considered

Python, TypeScript and Go were all viable — the API is equally reachable from
each, and the deployment target is a container either way. No alternative was
ruled out on technical grounds.

## Consequences

- `Prdb.Sdk` follows without a second decision. It targets `net8.0` and is
  consumed from `net10.0` unchanged, and it covers the identify, submission and
  fulfilment endpoints.
- `Prdb.Hashing` is available as a package rather than something to port (see
  [0004](0004-hashes-stay-bit-compatible-with-stash.md)). Had the answer been
  Python or Go, the hashing method would have had to be reimplemented from the
  specification.
- `dotnet build` and `dotnet test` are the verification commands.
- The image carries a .NET runtime alongside `ffmpeg`, which is a size the
  self-hosted audience notices. Worth watching, not worth reopening.
- The frontend being open means no frontend framework should be added until it
  is decided.
