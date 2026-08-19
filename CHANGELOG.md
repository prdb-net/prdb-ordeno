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

A file that has finished downloading is now asked about within a minute of
counting as finished, rather than whenever a five-minute timer next came round —
which, on a fresh installation, was reliably the wrong moment: the tool would sit
for five minutes showing files it had found and saying nothing about any of them.
The Downloads screen also says when it last asked and how many files it asked
about, so a run that found nothing ready is no longer indistinguishable from a
run that never happened.

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

A filed video now arrives with its metadata. A `movie.nfo` goes in next to it,
carrying the title, the release date, the studio and the cast, so the media
server shows the scene rather than a file name — and the performers are people
you can browse by rather than a line of text. What it says is fetched from prdb
at the moment it is written, so a title or a cast entry corrected since the file
was first recognised is what ends up in the library.

It is written when a video is filed, and at no other time. A `movie.nfo` you
wrote yourself is never touched: the video is filed next to it and your file
stays exactly as it is. The tool recognises its own by a comment at the top of
the file, so deleting that one line is how you take the file back. Replacing one
of its own is a write and a rename, so an interrupted run leaves either the old
file or the new one and never half of either, and prdb being unreachable means a
video that is filed with no metadata beside it rather than one filed with the
wrong metadata beside it. The plan on the Downloads screen says which of those
would happen before anything is written.

A filed scene can also get a picture. Turn on "Download one image for each
scene" under Settings → Library, and each video filed from then on arrives with
a `fanart.jpg` next to it, downloaded from prdb — one image, and no poster: the
images prdb has are the shape of the video, and a landscape image in the poster
slot is measurably worse in the library grid than no poster at all. It is off
until you turn it on, because it spends your connection and your disk.

Nothing is ever written over. An image already at that name stays exactly as it
is, whether the tool put it there last month or you chose it yourself this
morning — so deleting the file is how you ask for a fresh one, and the next
video filed into that scene brings it. A download that fails leaves nothing
behind at all and never fails a filing: the video is in the library and the row
says what did not arrive next to it. A scene prdb has no picture for is filed
without one and without a complaint, because that is the ordinary case rather
than a problem.

Setup has gained one optional step: the address of your media server and an API
key for it. Leaving it empty is a complete setup and there is no warning
anywhere about it — the tool files videos and writes the metadata file beside
each one either way, and your media server picks them up on its next scan.
Filling it in buys two things. A video the tool files is shown to the server
straight away, so it appears there without waiting for a scan. And the setup can
answer the question nothing else in either product would: Jellyfin parses release
dates against one exact format that is a server setting, and a server set to
anything else discards every date the tool writes without either side reporting
a thing — so the connection test reads that setting back and says so, next to the
field, while somebody is standing in front of it. It also looks for a video this
tool has filed, because a server that answers and holds none of them looks fine
and does nothing.

None of that can touch your files. A media server that is switched off, has
moved, or is answering with a key that was revoked does not stop, delay or fail a
single move; it is a line in the container's log and nothing else.

The workspace has areas with addresses: Downloads, Filing, Review and Settings,
each at its own URL. A reload keeps you where you were, the back button works,
and a link to the review queue is a link somebody can send. Filing is now an
area of its own rather than a card halfway down the downloads screen — it is the
one part of the tool that moves a file, and it has the room to say what it would
do to each video and what it did to each of them afterwards.

The settings have addresses too, one per thing there is to change: prdb, the
download directories, the library, and the media server, at `/settings/prdb` and
so on. One of them is on screen at a time instead of all four under each other,
so changing the library directory no longer means scrolling past the API key.
Setting the tool up for the first time is unchanged — the guided path is still
one column, numbered, with each step appearing as the one before it is answered,
because there the order is the point.

The lists of files are tables rather than stacks of paragraphs. One line per
file with the same columns down the page, and everything that explains a line —
where it would go, why it is blocked, what happens to a metadata file already
there — folded away under it until it is opened. Twenty files now fit on a
screen instead of four.

There is now a review queue, on a screen of its own, holding everything prdb
could not settle: the files several videos fit equally well, the ones only the
site could be read from, and the ones nothing matched. Where prdb named
candidates they are buttons — one press and the file is settled. Where it named
none, a search box opens already filled in with the file's own name, and
whatever prdb has under it can be picked from the results. Saying no is an
answer too: a file that is not a video, or one you do not want filed, is left
alone and stops coming back, without being deleted or hidden from what the tool
found.

Settling a file does not move it. It says what the video is, and filing is still
the button on the Downloads screen — from then on the file is filed like any
other, under what you said it is. Your answer outranks anything prdb says later,
and the only thing that forgets it is the file's contents changing, because at
that point it is a different video at the same name.

The first day is thousands of files, so the queue is worked a page at a time,
can be narrowed to one site, and a whole page of samples can be left alone in one
go. Every decision can be undone.

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

The log can be turned up, which until now it could not. Settings whose name
contains a dot — which is every one of them, since they name a logging category
— were discarded by the shell the container starts under before the application
ever saw them, with nothing said about it anywhere. `Logging__LogLevel__Prdb.Ordeno=Debug`
now reaches the tool and does what it says.

Configuration and the single password are set up in the browser on first run
rather than through the container environment.
