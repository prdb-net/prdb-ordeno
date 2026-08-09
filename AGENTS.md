# Repository Guidelines

A tool that organises video files using metadata from prdb. Open source, MIT.

It is a self-hosted web application, not a command-line tool: a long-running
service in a container, with a browser UI, that keeps filing new downloads
without being asked. Source and target storage arrive as Docker mounts. Read
`VISION.md` before designing anything — it is what these constraints are in
service of.

## Stack

The backend is **.NET 10**, with the SDK version pinned in `global.json` —
[ADR 0005](docs/adr/0005-dotnet-10-on-the-backend.md).

The frontend is **React with Vite and TypeScript** —
[ADR 0006](docs/adr/0006-react-and-vite-on-the-frontend.md). It builds to static
assets the backend serves: no server-side rendering, and **no Node in the
runtime image**. Node compiles the frontend in a build stage; the runtime stage
carries neither it nor `node_modules`. Dependencies stay few and boring.

The contract between the two is generated, not maintained: the host emits an
OpenAPI document and `openapi-typescript` turns it into types the frontend
compiles against, both committed and both checked by CI
([ADR 0014](docs/adr/0014-the-frontend-types-are-generated-from-openapi.md)).
Never edit the generated file, and give endpoints named response types — an
anonymous shape generates a type nobody can read.

Local state — review queue, operation log, hash backlog, configuration — lives
in **SQLite through EF Core**, in a file in the mounted data volume, with
migrations applied at startup —
[ADR 0007](docs/adr/0007-sqlite-through-ef-core-for-local-state.md). SQLite
takes one writer at a time, so a transaction spans a state change and never an
ffmpeg call or a request to prdb. This store is not a copy of prdb's corpus and
never becomes one.

The image ships `ffmpeg` and `ffprobe`, because computing a perceptual hash
decodes frames. Nothing may require the user to install anything on the host
beyond Docker.

## Scope of the first release

One media server: **Jellyfin**, with its layout validated against a real library
— [ADR 0008](docs/adr/0008-the-first-release-targets-jellyfin-only.md). Plex and
Emby follow on the same evidence. Sidecar writing gets a seam, not a second
implementation built for a server nobody has run yet.

## Language

**Everything in this repository is in English** — code, comments, documentation,
commit messages, branch names, PR titles and descriptions, CI job names, test
names, and anything visible to users of the tool. No exceptions.

## The one hard rule

**This tool moves, renames and deletes files that a user cannot get back.**
Treat every write path as the dangerous one, because it is:

- A destructive operation is opt-in, never the default. Deleting or overwriting
  is something the user turned on deliberately, not something that happens
  because nobody turned it off.
- Every write path can be asked what it would do without doing it, and that
  answer is exactly what the real run performs. In the UI this is a preview the
  user confirms; in the automatic runs that follow, it is what gets recorded and
  shown afterwards. A button is easier to press than a command is to type, so
  running unattended behind a web UI raises the stakes here rather than
  lowering them.
- Nothing is written on the strength of a partial or failed metadata lookup. An
  API error means stopping, not falling back to a guess at the filename. A
  scheduled run is no excuse to degrade: unattended is not the same as
  unsupervised, and the user reads the result later.
- Moves across filesystems are copy-then-verify-then-delete. A rename that turns
  into a copy under the hood must not lose the file when it fails halfway.

A plausible refactor that quietly weakens one of these does more damage than any
crash. When you touch file handling, this is what the tests are for.

## Where the metadata comes from

The prdb Public API. It is documented at <https://apidocs.prdb.net> and the
OpenAPI document is public, but the path is not guessable — the docs UI fetches
`/configuration.json` to discover it.

Authentication is an API key in the `X-Api-Key` header, over https. `GET /health`
is the only endpoint that works without a key.

**Do not hand-roll an HTTP client for it.** Use the `Prdb.Sdk` NuGet package
from `prdb-sdk`. It handles the parts that are easy to get wrong — in
particular refusing a redirect to a different origin, which every HTTP stack
would otherwise follow while carrying `X-Api-Key` off the API host, because they
strip only `Authorization`. It targets `net8.0` and is consumed from `net10.0`
unchanged.

Identification goes through `POST /videos/identify`, which walks the whole
recognition ladder server-side. **Do not rebuild that ladder here** out of the
individual lookup endpoints, and do not keep a local copy of prdb's corpus —
[ADR 0001](docs/adr/0001-identification-runs-in-prdb.md).

## Computing the hashes

The `osHash` and `pHash` values sent to that endpoint come from the
**`Prdb.Hashing`** NuGet package. It is separate from `Prdb.Sdk` because it
starts processes and needs ffmpeg; use both.

**Do not reimplement either hash, and do not "clean up" the one in the package.**
It looks wrong in several places and is wrong on purpose; a correction produces
values that match nothing, and no test here would notice —
[ADR 0004](docs/adr/0004-hashes-stay-bit-compatible-with-stash.md) explains
which places and why. The method is specified normatively in
[`docs/video-hashing.md`](https://github.com/prdb-net/prdb-sdk/blob/main/docs/video-hashing.md)
in `prdb-sdk`, with test vectors as data next to it. Read one of the two before
touching anything hash-shaped.

Two states from the package's contract that shape design here: `osHash` is
`null` for a file under 128 KiB, and a perceptual hash decodes 25 frames, so it
belongs in a background queue rather than the import path.

## Configuration and access

The container environment carries only what must exist before the application
starts: `ORDENO_DATA_DIRECTORY` (default `/data`, holding the SQLite file), the
port, and `PUID`/`PGID`/umask. Everything else — API key, sources, target,
layout, behaviour switches — is collected by onboarding and stored in the
database
([ADR 0009](docs/adr/0009-configuration-is-collected-by-onboarding.md)). A fresh
container therefore starts into onboarding and scans nothing until it is done.
Do not add an environment variable for a setting the UI owns.

Access is one password, no username, set during that same first run and hashed
with `PasswordHasher<T>`; sessions are opaque tokens in HttpOnly cookies, stored
as rows so they survive a restart and can be revoked, and sign-in is
rate-limited ([ADR 0010](docs/adr/0010-one-password-set-at-first-run.md)). There
is no default password. The setup path is the only unauthenticated write path in
the application and closes the moment a password exists — that transition has a
test, in `tests/Prdb.Ordeno.Host.Tests`.

**Endpoints are behind the password unless they say otherwise.** The
authorization fallback policy requires an authenticated user, so a new endpoint
is protected by default and `.AllowAnonymous()` is a deliberate act. If you find
yourself adding it, say in the same breath why a stranger may call that.

`ORDENO_RESET_PASSWORD=true` clears the password and every session at startup —
the documented way back in for someone who lost it, reachable only by whoever
can edit how the container starts. It warns loudly, because leaving it set means
clearing the password on every restart.

## Shipping

GitHub Actions builds the image and publishes it to Docker Hub for
`linux/amd64` and `linux/arm64`, tagged with the commit SHA, `latest` on the
default branch, and the version on a release
([ADR 0011](docs/adr/0011-images-are-built-by-github-actions-and-published-to-docker-hub.md)).
Documentation and Compose examples pin a version rather than `latest`: an
unattended tool that upgrades itself on the next NAS restart is a surprise.

The runtime base is `mcr.microsoft.com/dotnet/aspnet:10.0` with `ffmpeg` and
`ffprobe` from Debian. The container starts as root, and the entrypoint applies
`PUID`/`PGID` (default `1000:1000`) and `exec`s the application under that
identity — `exec`, so the app stays PID 1 and still receives `SIGTERM` mid-move
([ADR 0013](docs/adr/0013-the-image-is-debian-based-and-drops-privileges.md)).
**The entrypoint never chowns the media.** It touches ownership of the tool's
own data volume and nothing else.

## Layout

```
LICENSE           MIT
README.md         what the tool is
VISION.md         what it is for, and what it is deliberately not
CONTRIBUTING.md   how to report a bug, how to shape a commit
CHANGELOG.md      Keep a Changelog, Unreleased at the top
docs/adr/         decisions, and the alternatives they ruled out
docs/agents/      where issues live, where domain docs live

src/Prdb.Ordeno.Core            domain and application logic; no I/O
src/Prdb.Ordeno.Infrastructure  SQLite/EF Core, filesystem, Prdb.Sdk, Prdb.Hashing
src/Prdb.Ordeno.Host            ASP.NET Core: HTTP, auth, static assets, workers
src/Prdb.Ordeno.Frontend        React and Vite

tests/Prdb.Ordeno.Core.Tests
tests/Prdb.Ordeno.Infrastructure.Tests
tests/Prdb.Ordeno.Host.Tests           hosts the real application; see ADR 0015
```

This file holds the rules; `docs/adr/` holds why they are the rules. When a rule
here looks wrong, the ADR is where the answer is — and if it is genuinely wrong,
a new ADR supersedes the old one rather than editing it.

The solution file lives at the root. The dependency direction is fixed and the
build enforces it: `Core` references neither EF Core, nor `Prdb.Sdk`, nor the
filesystem — it declares what it needs as interfaces, `Infrastructure`
implements them, `Host` wires the two together, and nothing references `Host`.
That is what keeps the hard rule above enforceable rather than merely agreed:
the dangerous paths are reachable only through an interface, which is also what
makes asking a write path what it would do the same code path as doing it. See
[ADR 0012](docs/adr/0012-src-is-sliced-before-the-first-feature.md), which
replaced the earlier instruction to leave this open until the first feature.
A fifth project is a decision, not a habit.

The rule about the host is about `src/`: a test project may host the real
application, and none of it may be replaced with a double when it does —
[ADR 0015](docs/adr/0015-tests-may-reference-the-host.md).

`ArchitectureTests` in `Prdb.Ordeno.Core.Tests` reads the project files and
fails when a reference appears where the ADR says none may be. If you are about
to add one, that test is the conversation, not the obstacle.

Package versions are central, in `Directory.Packages.props`; project files carry
`PackageReference` without a version. NuGet auditing is on and warnings are
errors, so a package with a published advisory stops the build — the fix is a
newer version or a documented override, never a suppression.

## Commits

Subjects follow [Conventional Commits](https://www.conventionalcommits.org/):
`feat:`, `fix:`, `chore:`, `docs:`. Under about 72 characters, imperative.

Explain *why* in the body. A commit that says what the diff already shows is a
wasted opportunity; the interesting part is the reasoning that is no longer
visible once the code is in place.

Add a `CHANGELOG.md` entry under `## [Unreleased]` for anything a user would
notice. A refactor that changes no behaviour does not need one.

## Verifying a change

```
dotnet build
dotnet test
```

The destructive paths above get tests before they get a release, and those tests
run against a real filesystem in a temporary directory — the interesting
failures are the ones a mocked file layer cannot have: a half-finished
cross-device copy, a file still being written, a target path that already
exists. `TempDirectory` in `Prdb.Ordeno.Infrastructure.Tests` is where such a
test starts.

Touching the frontend means building it too:

```
cd src/Prdb.Ordeno.Frontend && npm run build
```

## Versioning

[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Pre-`1.0`, a minor
bump may break things.

## Agent skills

### Issue tracker

Issues live in this repository's GitHub Issues, via the `gh` CLI. See
`docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `CONTEXT.md` and `docs/adr/` at the repository root. See
`docs/agents/domain.md`.
