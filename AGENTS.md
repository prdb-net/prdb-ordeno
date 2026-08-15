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

The document is `src/Prdb.Ordeno.Host/openapi.json`, written by every build of
the host, and the types are `src/Prdb.Ordeno.Frontend/src/api/schema.d.ts`. One
command regenerates both:

```
cd src/Prdb.Ordeno.Frontend && npm run generate:api
```

An endpoint declares what it answers by returning it: `TypedResults` and a
`Results<...>` union put the responses in the signature, where the compiler
keeps them true. The sign-in endpoint is the exception — a 401 carrying a body
has no typed result behind it, so it declares its responses with `.Produces<T>`
and says so in a comment. Reach for that only when there is no typed result to
return.

The generator loads the host to read its endpoints and stops it where it would
start listening, so `Program.cs` skips the startup work in that process — see
the `GetDocument.Insider` check there before adding anything between building
the application and running it.

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

That validation is done, and the layout it produced is
[`docs/jellyfin-layout.md`](docs/jellyfin-layout.md): a Movies library, one
directory per scene, `movie.nfo`, and a set of rules that are narrower than the
documentation suggests. Four of them bite hard enough to name here, because a
writer that misses one produces a library that looks broken rather than one that
fails:

- `<premiered>` is parsed against **exactly** `yyyy-MM-dd`. An ISO timestamp is
  silently discarded, along with the production year.
- A performer must be an `<actor>` element with a `<name>` child, and `<type>`
  must be `Actor` — text directly inside `<actor>` is dropped, and an
  unrecognised type produces a person of kind `Unknown`.
- A second quality must be named `<scene> - [2160p].mkv`. Without the bracketed
  form the two files become two entries with identical names.
- The sidecar is XML and a title is arbitrary text, so `&`, `<` and `>` have to
  be escaped. An unescaped one makes Jellyfin discard the whole file without
  saying so.

Not every answer fills that shape. A scene with no release date drops the date
segment from its name and gets a sidecar with no `<premiered>` and no guessed
year, and a file prdb cannot name — including everything the site rung answered
for — is **not filed at all** and waits in the review queue
([ADR 0019](docs/adr/0019-a-missing-date-drops-the-segment.md)).

`docs/jellyfin-probe/` is the harness that established all of this against
Jellyfin 10.11.11. Re-run it before changing anything layout-shaped, and against
a new Jellyfin before claiming the layout still holds.

The tool may also be given a Jellyfin URL and API key, and works without them —
[ADR 0018](docs/adr/0018-the-jellyfin-connection-is-optional.md). They buy three
things: the release date format read back before it silently discards every date,
a refresh of an item whose sidecar was just rewritten, and the confirmation
onboarding ends on. **The filing path never depends on them.** A server that is
down, moved or answering with a stale key does not stop, delay or fail a move,
and no feature may require the connection to exist.

## Watching the download directories

Source directories are walked on a timer, not watched through the filesystem:
notifications do not cross an SMB or NFS mount reliably, and that is where this
audience's media sits — [ADR 0016](docs/adr/0016-directories-are-scanned-on-a-timer.md).

A file is a candidate only once **two scans have seen it unchanged**. Never
decide that from the file's modification time against the local clock: on a
share that timestamp comes from the NAS, and two clocks that disagree would
either hold everything back forever or release a file mid-download. Compare an
observation with the previous observation, both taken here.

The walk reads directory entries and opens nothing — it is work the container
does every few minutes for years. A scan writes to the tool's own tables and to
nothing else; the download directories are read-only to it, whatever comes
later.

## Filing

**Nothing is filed until somebody asks for it** —
[ADR 0022](docs/adr/0022-filing-happens-when-it-is-asked-for.md). There is no
filing timer, and its absence is a decision rather than an omission: the
unattended run arrives with the operation log and its undo
([#19](https://github.com/prdb-net/prdb-ordeno/issues/19)) and not one release
earlier. What makes that a schedule rather than a rewrite is that **the preview
is produced by the code that performs the run** — `FilingPlanner` decides
everything and writes nothing, `LibraryMoves` writes and decides nothing, and
the run works the plan out again as it reaches each file.

A filesystem cannot say whose a directory is, so the tool keeps one row per
filed video — scene, directory, name, quality. It is what tells a second quality
of a scene filed last year, which goes *into* that directory, from two different
scenes the layout gives one name, which must not. It is **not** the operation
log: it says what is true of the library now, a row whose file has gone is
deleted rather than kept, and the log lands next to it rather than out of it.

Three rules that a plausible refactor would quietly break:

- **A second quality relabels what is already there**
  ([ADR 0020](docs/adr/0020-a-second-quality-relabels-the-filed-file.md)). The
  filed file is renamed to carry its own label first, then the newcomer goes in
  next to it — that order, so an interruption leaves one correctly labelled file
  rather than a directory where only half of what is in it is labelled. Quality
  is compared as the **label** and never as the dimensions, and a file whose
  quality cannot be read is not filed at all.
- **A cross-filesystem copy is verified by size and `osHash`**
  ([ADR 0021](docs/adr/0021-a-copy-is-verified-by-size-and-os-hash.md)),
  computed fresh on both sides after the copy is flushed — never taken from what
  identification stored, which was read before the copy happened. A rename is
  only used where the tool can prove the two are on one filesystem: `File.Move`
  turns a cross-device rename into an unverified copy of its own.
- **A copy is staged in `.prdb-ordeno-incoming/` under the library root**, not
  in the scene directory. A directory with anything in it counts as occupied, so
  a part file left by a killed container would make a scene's own directory look
  like somebody else's the next time round.

## The sidecar

A filed video with a tidy name and no metadata next to it is not what anyone came
for: what the media server shows comes out of a `movie.nfo` in the scene
directory. **It is written as part of filing a video and at no other time**
([ADR 0024](docs/adr/0024-the-sidecar-is-written-by-filing-and-only-over-its-own.md)),
and *when* an existing one is refreshed is still an open question rather than an
omission.

`MovieNfo` builds the document and touches nothing; `Sidecars` puts it on disk and
decides nothing except what it finds at the path the moment before it writes.
Four rules that a plausible refactor would quietly break:

- **It goes in after the video, never before.** A sidecar written first leaves
  metadata in a directory holding no video when the move fails — and a directory
  with anything in it counts as occupied, so the retry would file the scene
  around its own directory.
- **The tool writes over its own and nothing else.** Its own is the one carrying
  the comment `Written by prdb-ordeno`; anything else, including a file that could
  not be read, is left exactly where it is. A hand-written sidecar is at the same
  name — `movie.nfo` is what a Movies library reads — so there is nothing to step
  around, and deleting that comment line is how a user takes the file back.
- **What it says is fetched when it is written**, in batches of fifty, not read
  off the identification row (ADR 0017) — a corrected title is most of the reason
  the file is worth writing. A lookup that fails writes nothing at all and the
  video is still filed.
- **A rewrite is a write and a rename.** Jellyfin does not care which way the
  change arrived (section 7), but a truncating write killed halfway leaves a
  document that parses nowhere, and an unparseable sidecar is discarded in silence.

The three shapes that fail silently — the date format, the actor element, the
escaping — are in `MovieNfoTests` rather than in comments, because none of them
produces an error anywhere.

## The review queue

What prdb could not settle waits for a person: several videos that fit equally
well, the site alone, or nothing at all. **A person's answer is stored in a table
of its own and outranks prdb's**
([ADR 0023](docs/adr/0023-a-persons-answer-is-kept-beside-prdbs.md)) — filing
reads the two in that fixed order, and nothing else may be added to the list
without another decision.

Four rules that a plausible refactor would quietly break:

- **The decision does not live on the identification row.** That row is replaced
  whole every time a file is asked about again, which is what keeps prdb's answer
  honest; a decision stored there survives until the next perceptual hash
  arrives. The scan forgets a decision in exactly one case, in the same statement
  that forgets the identification: **the bytes changed**. A decision about last
  week's file naming this week's is how the wrong scene ends up in the library
  under a deliberate-looking name.
- **A resolution arrives as a video id and nothing else.** The title, site and
  date on the row are fetched here, from prdb, because they become a directory
  name — a path built from what a page posted is a path built from unvalidated
  input.
- **Dismissal is an answer, not a deletion.** The file stays on disk, stays in
  the inventory and stays counted; it stops being offered, and it is never filed.
  Nothing in the queue may become a way to make files disappear.
- **Candidates are described once.** The identify endpoint names them as ids, so
  the words come from `POST /videos/batch` the first time a row is shown and are
  kept next to the id — ADR 0017's bargain, applied to the part of an answer that
  arrives without words. A candidate prdb does not know is still stamped as
  asked-about, or it would be asked about on every page view forever.

The queue is paged rather than capped, unlike the downloads screen: this is work
somebody has to get to the end of. The first day is thousands of files, so it is
narrowed by site and dismissed by the page, and resolving something never moves
it — filing is still the run somebody asks for (ADR 0022).

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

What comes back is stored per file, with enough of the video — title, date, site
— to put on a screen, and **a file is asked about once**
([ADR 0017](docs/adr/0017-prdbs-answer-is-stored-and-asked-for-once.md)). Only
two things make it worth asking again: its bytes changed, or a perceptual hash
arrived that the first question did not carry. Anything that re-asks on a timer
spends a rate-limited quota to be told the same thing, which is the request
pattern ADR 0001 exists to avoid.

That stored copy is for reading. Nothing is filed from it and nothing may grow
it into a corpus: what a sidecar is written from is fetched again when it is
written.

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

The queue takes one file at a time and only files the exact hash did not settle.
prdb still compares perceptual hashes for equality, so hashing a file it has
already recognised by its `osHash` spends minutes of somebody's evening to learn
what is already known. A file waiting for its hash holds nothing up: it is asked
about without one and asked again once it has one.

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

The published name is `prdbnet/prdb-ordeno`. Only the runtime stage of the
`Dockerfile` is architecture-specific: the frontend build and the .NET publish
run on the build machine's own architecture and produce output that does not
care where it lands, so an arm64 image costs emulation for `apt-get` and not for
MSBuild. Keep it that way — a runtime identifier in that publish is what would
put a Node build and a compiler under QEMU.

`docs/running-in-docker.md` is the user-facing half of all this, and a release
publishes it as the Docker Hub description. It is written for someone who has
never seen this repository, and it links to GitHub absolutely, because relative
links do not resolve once Docker Hub renders it.

## Layout

```
LICENSE           MIT
README.md         what the tool is
VISION.md         what it is for, and what it is deliberately not
CONTRIBUTING.md   how to report a bug, how to shape a commit
CHANGELOG.md      Keep a Changelog, Unreleased at the top
docs/adr/         decisions, and the alternatives they ruled out
docs/agents/      where issues live, where domain docs live

docs/jellyfin-layout.md  the layout the tool files into, and the evidence for it
docs/jellyfin-probe/     the harness that produced that evidence, and its output

Dockerfile          frontend, publish, and a Debian runtime with ffmpeg in it
docker/             the entrypoint, and the test that starts a built image
docker-compose.yml  an example to copy, pinned to a version rather than latest

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

Touching an endpoint's request or response means regenerating the contract and
committing what changes:

```
cd src/Prdb.Ordeno.Frontend && npm run generate:api
```

CI runs the same command and fails on a diff, so a stale generated file is a red
build rather than a frontend quietly compiled against a shape the backend no
longer sends.

Touching the `Dockerfile` or the entrypoint means starting what comes out of it:

```
docker build -t ordeno:local .
docker/smoke-test.sh ordeno:local
```

That is the same script CI runs, on both published architectures. It checks what
only a running container can: that the application ends up as `PUID:PGID`, that
the media it was pointed at keeps the owner it arrived with, and that
`docker stop` is a shutdown rather than a kill after the timeout. A change to
how the container starts that no test could have noticed is exactly the kind
this repository cannot afford.

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
