# 0007. SQLite through EF Core for local state

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

The tool has to remember things across restarts, and more of them than the
product description suggests: the review queue, the operation log that undo
reads, the backlog of files still waiting for a perceptual hash, what has
already been filed and where it came from, and the configuration the onboarding
path collects. None of that survives in memory on a box that reboots for
updates.

The audience decides the shape of the answer. This is a single-user appliance on
a NAS, set up once and left alone, delivered as one container with its storage
mounted in. Anything that asks the user to run a second container for the tool
to start has broken the promise the tool was installed for.

Two things this store is *not*: it is not a copy of prdb's corpus — the hash and
metadata lookups stay remote, per
[ADR 0001](0001-identification-runs-in-prdb.md) — and it is not the library.
It holds what happened locally and nothing else.

## Decision

SQLite, in a file in the mounted data volume, accessed through EF Core with
migrations applied at startup.

## Alternatives considered

- **PostgreSQL.** Rejected: it is the better database and the wrong deployment.
  It costs the user a second container, a password and a volume before the first
  scan runs, in exchange for concurrency this workload does not have — one
  process, one writer, a few thousand rows.
- **Plain files: JSON documents, or a log on disk.** Rejected: the operation log
  has to answer "what did this run do" and "undo exactly this", and the review
  queue has to be paged, filtered and counted while a background worker writes
  to it. That is a set of queries and a transaction boundary, which is what a
  database is. Two writers and no transactions is how a half-applied undo
  happens.
- **SQLite without EF Core (Dapper or raw ADO.NET).** Rejected on the long run
  rather than the first commit. Hand-written SQL against SQLite is pleasant to
  write and the schema will change for years — layouts, new rungs, new
  configuration — and it is the migrations, not the querying, that would be
  rebuilt by hand. EF Core is here for `Migrations`; nothing obliges the write
  paths to be expressed as LINQ where SQL is clearer.

## Consequences

- SQLite takes one writer at a time. Write-ahead logging is on, and the long
  operations — a scan over a library, a batch of moves — must not hold a write
  transaction open while they run. A transaction spans a state change, never an
  ffmpeg call or an HTTP request to prdb.
- The database file sits in the mounted data volume next to the configuration,
  so a rebuilt container keeps its state and a backup is a file copy. The
  documentation has to say that copying it while the tool runs needs the WAL
  files too, or the backup is a torn one.
- `Microsoft.Data.Sqlite` carries its own native library, `linux-arm64`
  included, which keeps the multi-arch image promised in ADR 0005 honest.
- Migrations run at startup, on a database the user cannot be expected to
  restore. A migration that fails stops the tool rather than continuing against
  a schema it does not understand, and the release that contains it says so.
- The operation log is append-only. Undo writes new rows describing the reversal;
  it never edits or removes the record of what happened, because the log is also
  what a bug report quotes.
- Storing absolute paths ties rows to the mount layout inside the container. How
  paths are recorded so that a changed mount does not orphan the history is a
  design question this decision creates and does not answer.
