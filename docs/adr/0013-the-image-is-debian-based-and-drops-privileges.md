# 0013. The image is Debian-based and drops privileges at start

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

The image has to carry two native things: `ffmpeg` and `ffprobe`, because
perceptual hashing decodes frames, and SQLite, which arrives as a native library
([ADR 0007](0007-sqlite-through-ef-core-for-local-state.md)). It is built for
`linux/amd64` and `linux/arm64`
([ADR 0011](0011-images-are-built-by-github-actions-and-published-to-docker-hub.md)),
and it runs on a NAS where the media it touches is owned by a specific user and
group that the container has to match, or every filed file lands with the wrong
owner.

Those two requirements pull in the same direction, which is what makes this one
decision rather than two: the smallest images are the ones without a package
manager to install `ffmpeg` with and without a shell to work out an identity in.

## Decision

The runtime base is `mcr.microsoft.com/dotnet/aspnet:10.0` — Debian — with
`ffmpeg` and `ffprobe` installed from the distribution.

The container starts as root and does not stay there. The entrypoint applies
`PUID` and `PGID` (defaulting to `1000:1000`) and `exec`s the application under
that identity, so the process the user's files are written by is never root.

## Alternatives considered

- **Alpine.** Rejected. The image is much smaller, and it costs musl: a second
  native SQLite variant to ship and an `ffmpeg` build far fewer people are
  running. Both are the kind of difference that behaves on the machine it was
  built on and surfaces on someone else's NAS, which is the worst place for this
  project to find a bug.
- **Chiseled or distroless.** Rejected, with regret — the attack surface is the
  smallest of the three and this application is worth protecting. But it has no
  package manager, so `ffmpeg` would have to be a static binary copied out of a
  build stage and maintained by us, and no shell, so the identity step above has
  nowhere to happen. It is paid for in exactly the two things this image needs.
- **A fixed non-root user, no remapping.** Rejected as the default: it is the
  cleaner container, and it moves the single most common support case — files
  filed with an owner the NAS user cannot read — into the documentation.
  Compose's own `user:` still works for anyone who prefers it.
- **Remap only when `PUID` is set, otherwise run as given.** Rejected: two start
  paths, each needing its own test, for a flexibility nobody asked for.

## Consequences

- Starting as root is deliberate and narrow. The entrypoint `exec`s rather than
  forks, so the application stays PID 1 and keeps receiving signals — a
  container that ignores `SIGTERM` is one that gets killed mid-move.
- **The entrypoint never chowns the media.** It fixes ownership of the tool's
  own data volume and nothing else. Recursively taking ownership of a library is
  slow on a NAS and is not this tool's business, whatever other images do.
- `ffmpeg` follows Debian stable and will lag. Frame decoding is not where
  ffmpeg moves fastest, so this is acceptable; if a real bug forces a newer
  build, the answer is a pinned backport rather than compiling from source in
  the image.
- The image is the largest of the three options. ADR 0005 already said the
  number that matters on a NAS is memory, not image size, and this decision
  spends the one to protect the other.
- The documentation has to teach `PUID`/`PGID` and umask properly — `VISION.md`
  already lists them among the things people get wrong.
