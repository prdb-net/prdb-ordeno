# 0001. Identification runs in prdb, not here

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

Recognising a file is a ladder: exact file hash, then perceptual hash, then a
stored file name, then the release name, then — failing all of those — the site
read out of the file name. Each rung needs prdb's corpus to answer.

The obvious way to build this is client-side: call the individual lookup
endpoints in order and stop at the first hit. Measured against the endpoints
that existed at the time, a first pass over 300 files cost about 115 requests,
roughly 100 of them a single `GET /predb/search-by-video` per file name. A
5,000-file library came to about 1,700 requests against an hourly and a monthly
quota. Almost all of that is protocol overhead.

The alternative usually reached for next is worse: mirror prdb's hash corpus
locally through the `/changes` feeds and match offline. That trades the request
count for a first run that synchronises for hours before the user sees anything
happen, and it is bulk extraction of the one dataset that distinguishes prdb
from a plain metadata database.

## Decision

Identification goes through `POST /videos/identify`: a batch of up to 200 files
per request, one result per file, mapped back through a client-assigned `ref`.
The response names the rung that matched (`matchedBy`) and how strongly
(`confidence`), and when several videos fit equally well it returns them as
candidates rather than choosing one.

The ladder is not reimplemented here, and no local copy of prdb's corpus is
kept. The endpoint was proposed and built for this tool (prdb#27).

## Alternatives considered

- **Walk the ladder client-side over the individual lookups.** Rejected: about
  one request per file, and it puts the matching rules in the one place that
  cannot see the data they run against, so every improvement would need a client
  release.
- **Mirror the corpus locally via the `/changes` feeds.** Rejected: hours of
  synchronising before first use, and it is exactly the bulk extraction the API
  should not invite. `/videos/identify` inverts the direction — a caller only
  ever receives rows about files it demonstrably already has.

## Consequences

- A first pass over an existing library costs a handful of requests instead of
  one per file. That is the difference between a setup the user watches finish
  and one they leave running overnight.
- Matching improves without this tool changing.
- File names and hashes are sent to prdb for every file examined. This is not
  optional and not anonymous, because it is what identification *is*. It belongs
  in onboarding, stated plainly.
- The confidence scale and the candidate list define the review queue: an
  ambiguous answer is a question for a person, not a coin toss.
- We depend on an endpoint with one implementation. If it were withdrawn, the
  client-side ladder would have to be built after all — accepted knowingly.
