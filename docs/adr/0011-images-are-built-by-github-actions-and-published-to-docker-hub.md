# 0011. Images are built by GitHub Actions and published to Docker Hub

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

[ADR 0005](0005-dotnet-10-on-the-backend.md) promised `linux-arm64` from the
first image, because Synology and single-board hardware are the described
audience. That makes the build multi-arch from the start, which makes it a CI
concern rather than something done on a laptop.

Where the image is published is the more consequential half of the question. The
repository is on GitHub, so its own registry is the frictionless answer. But
this audience does not usually find software by browsing a registry — they find
it through Unraid's Community Applications, through Portainer's search box, or
through a Synology dialog, and those all look at Docker Hub first.

## Decision

GitHub Actions builds the image and publishes it to Docker Hub,
`linux/amd64` and `linux/arm64`, tagged with the commit SHA, `latest` on the
default branch, and the version on a release.

## Alternatives considered

- **GHCR.** Rejected on reach, not on merit. It needs no extra account and no
  extra secret, and the token it uses is the one CI already has. What it does
  not have is the audience: an image nobody can find in the interface they
  install software with has a distribution problem no amount of README fixes.
- **Both registries, GHCR primary and Docker Hub as a mirror.** Rejected as
  premature. It doubles the push and gives two places where a tag can be
  missing or stale, which is a support problem in exchange for redundancy
  nobody has needed yet. Adding GHCR later is a few lines in the workflow.

## Consequences

- CI needs a Docker Hub account and an access token in the repository secrets.
  That is a credential to rotate and an account whose loss would strand the
  published name — worth writing down where the project's other operational
  facts live, not in this repository.
- Anonymous pulls from Docker Hub are rate-limited. A NAS that pulls on a
  schedule can hit that, and the documentation should name it as a cause rather
  than let it look like a broken image.
- The Docker Hub page *is* the landing page for a large part of this audience.
  Its description is published from the repository as part of the release, not
  maintained by hand in a web form.
- `linux/arm64` is built either through emulation or on a native runner. Whether
  emulated builds are fast enough is a measurement to make once, not an
  assumption to carry: `ffmpeg` is not compiled here, but the frontend build and
  the .NET publish are not free either.
- The tag on a release is what the documentation pins. `latest` exists for people
  who want it, and the Compose examples should not use it silently — an
  unattended tool that upgrades itself the next time the NAS restarts is a
  surprise, not a feature.
