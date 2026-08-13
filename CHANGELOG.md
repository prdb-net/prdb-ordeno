# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Entries describe what changed for someone *using* the tool. A refactor that
moves a thousand lines but changes no behaviour is not worth an entry; a
renamed flag or a different default is, however small the diff.

Until the first release the version is `0.x`, and a minor bump may break
things — see SemVer's clause on initial development.

## [Unreleased]

Nothing released yet. The repository holds its licence, its project
documentation and the scaffolding the application will be built in: a .NET 10
solution, a React frontend that Vite builds into the backend's static assets,
and the test projects. The tool now creates and migrates its SQLite database in
the directory `ORDENO_DATA_DIRECTORY` names, `/data` by default. Nothing is
identified, moved or filed yet.

Access exists: a fresh installation sets one password on first use, and
everything else is behind the session cookie it hands out. There is no default
password. `ORDENO_RESET_PASSWORD=true` clears the password and every session at
startup for someone who has lost it.

Onboarding exists, in the browser. A fresh container walks from the password
through the prdb API key, the directories downloads arrive in, and the directory
the library lives in with the layout that reads it. Nothing is stored before it
has been checked: the key is checked against prdb, and each directory against
the filesystem the container can actually see — a path nothing is mounted at, a
source it may not read and a library it may not write to each say so next to the
field. Each download directory also says whether videos will be renamed into the
library or copied and deleted, which depends on the two sitting on one
filesystem. Until onboarding is finished the tool scans nothing and says so, and
afterwards the same screen is the settings — where it says plainly that nothing
is filed yet either, because identification and filing are not built. The stored
API key is never sent back to the browser and never written to a log.

The tool now looks in the download directories. Every few minutes — and
whenever the new Downloads screen is asked to — it walks each of them and
reports the videos it finds, saying for each whether it has finished being
written or is still arriving. A file counts as finished only once two scans have
seen it unchanged, so a download in progress is waited out rather than acted on,
and a directory that has gone away says so instead of appearing empty. Nothing
in the download directories is read, moved, renamed or written by this: the tool
finds files and reports them, and identification and filing are still to come.

The stack is .NET 10 on the backend, React with Vite in the browser, and SQLite
for local state. The first release targets Jellyfin; Plex and Emby follow after
it.

There is an image. It is Debian-based, brings its own `ffmpeg`, and honours
`PUID`, `PGID` and `UMASK`, so what the tool writes carries the owner and the
permissions the NAS expects — and it never changes the ownership of the media
it was pointed at, only of its own data directory. `docker stop` reaches the
application itself, which matters for a tool that may be in the middle of moving
a file. It is published to Docker Hub as `prdbnet/prdb-ordeno` for
`linux/amd64` and `linux/arm64`, tagged with the commit, with `latest`, and with
the version on a release; [docs/running-in-docker.md](docs/running-in-docker.md)
covers the mounts and the settings people get wrong, and there is a Compose
example to copy.

Configuration and the single password are set up in the browser on first run
rather than through the container environment.
