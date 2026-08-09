# 0006. React and Vite on the frontend

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

[ADR 0005](0005-dotnet-10-on-the-backend.md) settled the backend and left the
browser side open with one constraint: whatever it is has to build to static
assets the backend serves, because a framework with a server-side runtime of its
own would put a second runtime into an image that already carries `ffmpeg`. That
constraint rules out a deployment mode rather than a framework, so all the usual
candidates were still on the table: React, Vue, Svelte and Blazor WebAssembly.

What decides between them is not the constraint but the screens. `VISION.md`
says the review queue, the onboarding path and the "what is it about to do"
preview *are* the product, and the review queue has to survive a first run over
an existing library of thousands of files. That is a virtualised table with
inline resolution of ambiguous matches, not a form.

## Decision

React with Vite and TypeScript. It builds to static assets that the ASP.NET Core
host serves; no server-side rendering, and no Node in the runtime image.

## Alternatives considered

- **Vue 3 with Vite.** Rejected, narrowly. It satisfies the same constraint with
  the same build pipeline, and less ceremony. React wins on the two components
  this UI actually leans on — a virtualised table over thousands of rows, and a
  preview of a pending change — where the mature, maintained options are
  React-first and the alternatives elsewhere are ports of them.
- **Svelte or SvelteKit.** Rejected: the smallest bundle of the four, but
  SvelteKit has to be deliberately pinned to a static adapter or it brings the
  server this decision is avoiding. A framework whose default configuration
  violates the constraint is a footgun to hand to a future contributor.
- **Blazor WebAssembly.** Rejected: one language for the whole repository is a
  real attraction, but it pays for it with a multi-megabyte runtime download
  before the first paint — on a tool whose first impression is an onboarding
  path — and the interaction density of the review queue is where its
  round-trips are least comfortable.

## Consequences

- Node and npm are build-time dependencies, never runtime ones. The image is
  built in stages: Node compiles the frontend, the result is copied into the
  host's `wwwroot`, and the runtime stage carries neither Node nor
  `node_modules`. `AGENTS.md`'s promise that the user installs nothing beyond
  Docker is unaffected.
- `dotnet build` alone no longer produces a complete application. Whatever wires
  the frontend build into it has to be explicit enough that a contributor who
  runs the documented commands gets a working UI, not a blank page.
- Routing happens in the browser, so the host serves `index.html` for unknown
  non-API paths. That fallback must not swallow API 404s, which are a different
  answer to a different question.
- The UI is behind the single password (`VISION.md`), and it is a self-hosted
  tool on hardware that may have no internet access. Nothing loads from a CDN;
  fonts and assets ship in the image.
- React's ecosystem moves faster than this project will. Dependencies stay few
  and boring on purpose — a review queue does not need a state management
  library — because an unattended appliance is a bad place to discover that a
  transitive package went unmaintained.
- The frontend is a separate project under `src/`, built by Vite into the host's
  static assets. Exactly how the two are laid out follows from the first real
  feature, per `AGENTS.md`.
