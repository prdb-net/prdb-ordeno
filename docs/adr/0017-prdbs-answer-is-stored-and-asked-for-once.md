# 0017. prdb's answer is stored per file, and asked for once

- **Status:** Accepted
- **Date:** 2026-08-13

## Context

[ADR 0001](0001-identification-runs-in-prdb.md) settles where identification
happens: one `POST /videos/identify` per batch of up to two hundred files, the
ladder walked server-side. It also says, twice, that no local copy of prdb's
corpus is kept.

Building the caller raised two questions that decision does not answer.

**What does the tool keep of an answer?** The endpoint returns a `videoId`, the
rung that matched and a confidence. That is enough to file with and useless to
read: a row saying `0f3a…` recognised by `OsHash` tells the user nothing, and the
screen this milestone exists to fill is a list of their own files. The site is no
help either — it comes back on its own only on the site rung, and otherwise
arrives inside the video document.

The two ways to get a readable answer are asking for the video documents with the
identification (`includeVideoDetails`), or storing the ids and resolving them
through `POST /videos/batch` when the screen is drawn.

**When is a file asked about again?** A first run over an existing library leaves
thousands of files that matched nothing, because most people's downloads are not
in prdb yet. If a run asks about everything it does not yet know, every five
minutes, then a library that is 90% unidentified spends the hourly quota
answering the same question forever.

## Decision

The answer is stored per file, in a table of its own, and it carries the video's
title, release date and site alongside the identifiers.

That copy is for reading. Nothing is filed from it, and nothing may treat it as a
corpus: when a sidecar is written, what it is written from is fetched again.
Rows arrive only for files the user demonstrably has, they are replaced whole by
the next answer, and they are deleted with the file.

A file is asked about **once**. Two things make it worth asking about again, and
they are the only two:

- **Its bytes changed.** The scan notices, and clears the hashes and the answer
  together — an answer about different bytes is not an answer about this file.
- **A perceptual hash arrived that the question did not carry.** The row records
  whether it did, so this happens exactly once per file rather than on every run.

Everything else — a corpus that has grown since, a user who wants another look —
is the manual run behind the button, and later the review queue.

## Alternatives considered

- **Store identifiers only, resolve titles when the screen is drawn.** Rejected.
  It spends a request against the quota every time somebody opens a page, it
  makes the screen empty whenever prdb is unreachable, and it puts a network call
  in the path of rendering a list. The response is bigger with the documents in
  it, but it is one response per two hundred files rather than one per page view,
  and it is paid for once.
- **Re-ask about everything unidentified on every run.** Rejected: it is the
  request pattern ADR 0001 exists to avoid, arrived at from the other end.
- **Re-ask on a long timer — weekly, say.** Not rejected on the merits, only
  deferred: it is a sensible thing to want once the corpus is known to grow under
  a given user's files, and it belongs with the metadata refresh decision that
  `VISION.md` already lists as open. Adding it now would be a schedule nobody has
  a measurement for.
- **Keep the answer on the `DiscoveredFiles` row.** Rejected: that row is an
  observation of what is on disk, and this is a claim about what the video is.
  Separating them is what makes "the bytes changed, so forget what we were told"
  one delete rather than six columns somebody has to remember to clear.

## Consequences

- The downloads screen reads as sentences, works while prdb is down, and costs no
  request to draw.
- The local store holds a title, a date and a site name per file the user has.
  That is a copy of what prdb said, and the line against becoming a corpus is
  drawn at "only about files we have, only what fits on a row, never filed from".
- A file that prdb learns about tomorrow stays unidentified until something asks
  again. The button is the answer today; the review queue and the refresh policy
  are where a better one belongs.
- The perceptual backlog has a reason to exist beyond completeness: it is the one
  thing that makes the tool ask a second time on its own.
