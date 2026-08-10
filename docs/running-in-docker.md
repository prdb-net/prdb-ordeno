# Running prdb-ordeno in Docker

`prdb-ordeno` files freshly downloaded video files into a library a media server
can read, using metadata from [prdb](https://prdb.net). It is a long-running
service with a browser UI, not a command-line tool: it keeps filing new
downloads without being asked.

Docker is the supported way to run it. The image brings what it needs with it —
`ffmpeg`, among other things — so there is nothing to install on the host beyond
Docker itself.

- Source: <https://github.com/prdb-net/prdb-ordeno>
- Releases: <https://github.com/prdb-net/prdb-ordeno/releases>
- Issues: <https://github.com/prdb-net/prdb-ordeno/issues>

## Where this stands

**This version sets itself up and stops there.** It takes a password, checks
your prdb API key, and checks the directories you point it at. It does not yet
identify, rename or move anything — that is what the first release adds, and
the rest of this document describes the tool that release will be.

Running it now still answers something worth knowing: whether it works on your
hardware. The identity it files under, the mounts, whether the library you
pointed it at is really writable — all of that is better found out before it
starts touching files than after.

## Before you start

- **A prdb account with an API key.** The tool identifies nothing without one,
  and it will ask for it during the first run.
- **Storage you can mount into a container**: the directory your downloads land
  in, and the directory your library lives in.

## Running it

```yaml
services:
  ordeno:
    image: prdbnet/prdb-ordeno:0.1.0
    container_name: prdb-ordeno
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      PUID: 1000
      PGID: 1000
      UMASK: "002"
    volumes:
      - ./data:/data
      - /srv/media:/media
```

Pin a version rather than `latest`. This tool moves files, and an unattended
tool that upgrades itself the next time the NAS restarts is a surprise rather
than a feature. The current version is on the releases page above.

The same thing without Compose:

```
docker run -d --name prdb-ordeno \
    -p 8080:8080 \
    -e PUID=1000 -e PGID=1000 -e UMASK=002 \
    -v /srv/appdata/ordeno:/data \
    -v /srv/media:/media \
    prdbnet/prdb-ordeno:0.1.0
```

## The first run

Open `http://<host>:8080`. A fresh installation asks you to set a password —
there is no default one, and there is no username — and then walks through the
prdb API key, the directories your downloads arrive in, and the directory your
library lives in with the layout that reads it.

Nothing is stored before it has been checked. The API key is tried against prdb,
and every directory against the filesystem the container can actually see, so a
path nothing is mounted at, a source it may not read and a library it may not
write to each say so next to the field rather than at three in the morning.

Until that is finished the tool scans nothing. Afterwards the same screen is
where you change any of it.

## The mounts

| Mount | What it is |
| --- | --- |
| `/data` | The tool's own state: the SQLite database holding your password and your configuration, and later the review queue and the operation log. |
| Your media | Whatever you mount your downloads and your library from. The paths inside the container are yours to choose; you point the tool at them during the first run. |

Two things are worth getting right here.

**Keep `/data` on local storage.** SQLite on an SMB or NFS share is a way to
corrupt a database, not a way to back one up. Back the directory up by copying
it; do not run the tool out of a network share.

**Mount the downloads and the library from one filesystem.** Filing a video is
then a rename — instant, and nothing is ever half-copied. Across two
filesystems it becomes copy, verify, delete: correct, but it moves every byte,
which on a NAS is the difference between a moment and an hour. Mounting one
parent directory that holds both, as `/srv/media:/media` does above, is the
simplest way to be sure. The tool tells you which of the two you are getting for
each download directory while you are still setting it up.

## PUID, PGID and umask

The container starts as root, works out the identity you asked for, and runs the
application as that user. Everything it files is owned by them.

Set `PUID` and `PGID` to the user and group your library already belongs to. On
Linux or a NAS shell, `id yourusername` prints both. Getting this wrong is the
most common problem people have with tools of this kind: the files are filed
correctly and then nothing else can read them.

`UMASK` decides the permissions on what the tool writes. `022` gives the group
read access, `002` gives it read and write — the latter is usually what a
library shared between several accounts wants.

Two things this deliberately does **not** do:

- **It never changes the ownership of your media.** Only the tool's own `/data`
  directory. Recursively taking ownership of a library is slow, and it is not
  this tool's business, whatever other images do.
- **It does not need `PUID` at all if you would rather use Docker's own
  `user:`.** Start the container as a non-root user and the tool leaves the
  question alone; `/data` then has to be writable by that user already.

## When the library is on a network share

SMB and NFS do not carry ownership the way a local disk does, so the mount
invents some. A CIFS line in `/etc/fstab` usually looks about like this:

```
//server/VideoData  /mnt/videodata  cifs  credentials=…,uid=1000,gid=1000,file_mode=0755,dir_mode=0755
```

Everything under that mount then belongs to `1000:1000` with those permissions,
regardless of what the server has stored. If `PUID` is anything other than
`1000`, the tool is "other": it may read the directories and enter them, and it
may not write. That is exactly what onboarding reports, and it is telling the
truth — the same `touch` fails from a shell on the host.

Two ways out:

- **Point `PUID` and `PGID` at the ids the mount forces** — the `uid=` and
  `gid=` above. Nothing outside the container changes, and the share is none
  the wiser: the SMB session authenticates as whoever `credentials=` names, so
  the local ids only decide whether the kernel lets the write through in the
  first place.
- **Give your group write access on the mount**: `gid=<your group>`,
  `dir_mode=0775`, `file_mode=0664`, then remount. This is the one to pick when
  another account has to keep writing there too. Unmount fails while the
  container is running, so stop it first.

`UMASK` does nothing on a mount like this. `file_mode` and `dir_mode` decide the
permissions on everything the share shows, and the tool's mask cannot argue with
them.

## Environment variables

Everything the tool asks about during the first run lives in its database, not
here. What is left is what has to exist before the application starts.

| Variable | Default | What it does |
| --- | --- | --- |
| `PUID` | `1000` | The user filed files belong to. |
| `PGID` | `1000` | The group filed files belong to. |
| `UMASK` | `022` | The permission mask for everything the tool writes. |
| `ORDENO_DATA_DIRECTORY` | `/data` | Where the database lives. Change the mount rather than this, unless you have a reason. |
| `ASPNETCORE_HTTP_PORTS` | `8080` | The port inside the container. To reach it on another port, remap it on the host side (`-p 9000:8080`) instead. |
| `ORDENO_RESET_PASSWORD` | unset | See below. |

## If you have lost the password

Start the container once with `ORDENO_RESET_PASSWORD=true`. That clears the
password and every session at startup, and the next visit to the UI sets a new
one. Your configuration is untouched.

Remove it again afterwards. Left in place, it clears the password on every
restart — the tool warns loudly in its log about exactly that.

## Tags and architectures

Images are published for `linux/amd64` and `linux/arm64`, which covers x86 NAS
hardware and the ARM boards and newer Synology models alike.

| Tag | What it points at |
| --- | --- |
| `0.1.0` | A release. This is what documentation and Compose files should pin. |
| `latest` | The tip of the default branch. Fine for trying the tool out, a poor idea for something that runs unattended. |
| `<commit sha>` | Exactly one commit. Useful for reproducing a report. |

Anonymous pulls from Docker Hub are rate-limited per IP address. A NAS that
pulls on a schedule, or a household behind one address, can run into that: the
symptom is a pull that fails with `toomanyrequests`, and the fix is to log in to
Docker Hub on the host or to pull less often. It is not a broken image.

## Stopping and updating

`docker stop` sends `SIGTERM`, which the tool receives directly and acts on: it
finishes what it is in the middle of rather than being killed once the timeout
runs out. That matters here, because what it may be in the middle of is moving
one of your files.

To update, pull the new version and recreate the container. The database in
`/data` carries your configuration across, and the schema is migrated at
startup. Read the release notes first — before 1.0, a minor version may change
behaviour.

## When something goes wrong

The container log is the place to look; the tool writes what it did and why
there. A bug report is most useful when it says which of these it was:

- **A file was moved, renamed or skipped when it should not have been.** That is
  this tool's logic. Include the directory layout before and after.
- **The metadata was wrong.** The tool did what it was told and prdb's answer
  was wrong. That belongs upstream, though report it either way if you are not
  sure.
- **It crashed.** The log around the failure, and what it was pointed at.

<https://github.com/prdb-net/prdb-ordeno/issues>
