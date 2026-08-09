# 0009. Configuration is collected by onboarding, not by the environment

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

The tool needs to know an API key, one or more source directories, a target
directory and a layout before it can do anything. Two places can hold that. It
can arrive as environment variables in the Docker Compose file, which is what
this audience is used to from every other container on their NAS. Or the tool
can ask for it in the browser on first run, which is what `VISION.md` describes
as onboarding: a short guided path ending with the user watching the first batch
get filed.

The two are not merely different syntax for the same thing. Whichever owns a
setting decides what onboarding *is* — a guided path, or a read-only display of
what someone already typed into a YAML file.

## Decision

The container environment carries only what has to exist before the application
can start: the data directory, the port, and the process identity (`PUID`,
`PGID`, umask). Everything else — API key, sources, target, layout, the
behaviour switches around duplicates and contributions — is collected by
onboarding and stored in the database.

## Alternatives considered

- **Everything in environment variables.** Rejected. It is the familiar shape
  for the audience and it is genuinely declarative, but it puts the API key in
  the Compose file, turns "set up once" into editing YAML and restarting a
  container, and reduces the onboarding path — which `VISION.md` calls part of
  the product — to a status page. Validation also arrives at the worst moment:
  a wrong target directory becomes a container that will not start rather than
  a message next to the field.
- **Environment overrides the database when set.** Rejected, though it is the
  tempting compromise and would make a fully declarative deployment possible.
  Every setting would have two sources, each field in the UI would need a
  disabled state explaining why it cannot be edited, and every support
  conversation would start by establishing which source is live. That is a lot
  of permanent complexity bought for a deployment style this tool does not
  target.

## Consequences

- A fresh container starts into onboarding, not into a running loop. Until it is
  completed the tool scans nothing, and it says why.
- The database is the configuration, so the promise that configuration survives
  a container rebuild is the same promise as
  [ADR 0007](0007-sqlite-through-ef-core-for-local-state.md)'s mounted data
  volume. One volume to mount, one file to back up.
- The API key is stored by the tool. It is not written to logs, not returned to
  the browser after it has been saved, and the documentation should not pretend
  a file on the user's own NAS is a secret store.
- Changing a setting is a UI action at runtime, which means settings change
  while work is in flight. A scan that started under the old target directory
  finishes what it was doing or stops; it does not read configuration afresh
  halfway through a batch.
- No Compose-only deployment. Someone who wants to configure the tool without
  ever opening the UI cannot, and that is the trade this decision makes
  deliberately.
