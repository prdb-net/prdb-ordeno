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
finds files and reports them.

The tool now works out what those files are. Once a download has finished, it
asks prdb — in batches rather than one request per file, so a library of
thousands costs a handful of requests — and the Downloads screen says what each
file was recognised as and which rung of the ladder got there. Four answers are
possible and they are shown as four different things: the video, several videos
that fit equally well and no choice made between them, the site alone when the
scene could not be worked out, and nothing. A file is asked about once; it is
asked again only if its contents change. prdb being down, refusing the key or
running out of quota stops the run and says so, and leaves every file exactly as
it was — nothing is ever written on a partial answer. Perceptual hashes are
computed in the background, one file at a time, for the files an exact hash did
not settle, and those files are then asked about again.

Onboarding and the Downloads screen both say plainly that the name, size and
hashes of every file examined are sent to prdb. That is what identifying them
is; the files themselves are never uploaded.

The tool now files what it recognises, and does it when you ask. The Downloads
screen shows what would happen to every recognised video before anything moves:
which directory it would go into, what it would be called there, whether the
move is instant or a copy that takes as long as the file is large, and which
files would be left alone. A button carries exactly that out. Nothing is filed
on a timer — that arrives together with the log that can undo a run, and not
before.

The download directory ends up emptier by exactly the files that were filed.
Within one filesystem a video is renamed into place, which is instant and cannot
half-finish; across two it is copied, checked against the original and only then
deleted, so a container stopped halfway leaves the video exactly where it was.
Nothing is ever written over: a name that is taken stops the filing and says so.

The same scene at a quality the library already holds is not filed and not
deleted — it stays where it is and says why. At a quality the library does not
hold, it joins the copy that is there as a second version, and the file that was
filed first is renamed to carry its own quality so the media server lists both
by their resolution instead of one of them by its whole file name. Two different
scenes the layout would give one name do not share a directory, which would
otherwise merge them into a single entry.

A video whose quality cannot be read is not filed, because without it the tool
cannot tell a second quality from a second copy. Neither is one prdb could not
name: that waits for the review queue.

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
