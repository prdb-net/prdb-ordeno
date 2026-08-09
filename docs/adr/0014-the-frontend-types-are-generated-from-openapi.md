# 0014. The frontend's API types are generated from the OpenAPI document

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

The frontend and the backend are separate builds in one repository
([ADR 0006](0006-react-and-vite-on-the-frontend.md),
[ADR 0012](0012-src-is-sliced-before-the-first-feature.md)), and everything they
say to each other goes over the HTTP API. Nothing about that contract is checked
by either compiler on its own: the backend can rename a field, the frontend
still compiles, and the mistake surfaces as an empty column in the review queue
rather than as a build error.

The screens this matters most for are the ones `VISION.md` calls the product —
the review queue and the preview of what is about to happen. A silently missing
field there is not a cosmetic bug; it is the user confirming an operation
against an incomplete picture.

## Decision

The host emits an OpenAPI document at build time, and `openapi-typescript`
generates TypeScript types from it into the frontend. Both the document and the
generated types are committed, and CI regenerates them and fails if the result
differs from what is checked in.

Requests themselves stay plain `fetch` against those types. This generates a
description of the API, not a client for it.

## Alternatives considered

- **A full generated client (Kiota).** Rejected, though it is the tool
  `prdb-sdk` uses for its own clients, so the mechanics are known here. It
  brings a runtime library into a bundle where `fetch` and a type declaration
  do the whole job, and ADR 0006 committed to keeping the frontend's
  dependencies few and boring.
- **Hand-written types.** Rejected: nothing keeps them true. The drift is
  invisible until someone opens the page, which is exactly the failure mode the
  generated types exist to convert into a build error.
- **Generating during the build without committing the result.** Rejected: the
  point of committing them is that a change to the API contract appears as a
  diff in review, where someone can notice that a field the UI relies on has
  quietly changed shape.

## Consequences

- A changed response shape breaks the frontend build. That is the feature.
- Regenerating is a documented command, and forgetting it fails CI rather than
  producing a subtly stale frontend.
- The generated file is not edited by hand, and a review that sees it edited
  should treat that as the bug.
- The API has to be described well enough for the document to be worth
  generating from: endpoints that return anonymous shapes produce types nobody
  can read. This pushes toward named response types on the backend, which is a
  cost this decision imposes on `Host` rather than on the frontend.
- prdb's own API is untouched by this. It is reached from the backend through
  `Prdb.Sdk` and never from the browser — the API key must not travel there.
