# 0016. Download directories are scanned on a timer, and readiness is observed twice

- **Status:** Accepted
- **Date:** 2026-08-13

## Context

The tool has to notice that a download finished. Two questions come with that:
how it learns a file is there, and how it decides the file is finished.

The obvious answer to the first is filesystem notifications — `inotify` on
Linux, `FileSystemWatcher` in .NET. They are unreliable in exactly this tool's
deployment: the media sits on an SMB or NFS mount, where changes made by another
host produce no local event at all, and a container's view of a bind mount adds
another layer that events do not always cross. A watcher also loses events under
a burst and has no way to say so, which for this tool means a file that is never
filed and no record of why.

The second question is harder than it looks. A file appearing in a download
directory says nothing about whether it is complete: it may be growing, it may
be a name a client pre-created, it may be an archive halfway through being
extracted. Acting on one costs a hash of half a file, a wrong identification,
and — once filing exists — a move of something the download client still has
open.

The tempting test is the file's own modification time against the container's
clock: if nothing has been written for a minute, it is finished. That comparison
is between two clocks that need not agree. An SMB share stamps files with the
NAS's clock, and a NAS whose time is a few minutes ahead makes every file look
freshly written, permanently. Nobody would find that quickly, because the tool
would simply appear to do nothing.

## Decision

Directories are walked on a timer — every five minutes — rather than watched.
The walk is also what a person triggers from the UI, so there is one code path
and not a fast one and a careful one.

A file counts as finished when two scans have seen it with the same size and
modification time, and the quiet period has elapsed **between those two
observations**, both timestamped by the container's own clock. The file's
modification time is only ever compared with the previous reading of itself,
never with the clock here.

The observation is stored, so readiness survives a restart: a container that
comes back up does not put a settled library through its quiet period again.

## Alternatives considered

- **Filesystem notifications, with a periodic scan as a fallback.** Rejected for
  the first release. It is two mechanisms where one is enough, and the one that
  fires first is the one that cannot be trusted on this audience's storage. The
  scan is what has to be correct either way, so it is what gets built.
- **Deciding readiness from modification time against the local clock.**
  Rejected: it is a comparison between two clocks that belong to different
  machines, and it fails silently and permanently when they disagree.
- **Opening the file to see whether anything else holds it.** There is no
  portable answer. The download client is in another container or on another
  host, Linux advisory locks say nothing about that, and on a network share
  there is nothing to ask.

## Consequences

- A finished download is picked up within about five minutes, and one that is
  still being written costs another interval. The trade is deliberately on the
  side of waiting.
- A download client that pre-allocates a file and then stalls indefinitely will
  eventually look settled. Nothing is moved on that basis in this release; when
  filing exists, the protection is identification — a partial file matches no
  hash and no release name, so it lands in the review queue rather than in the
  library.
- The scan interval and the quiet period are constants with their reasoning
  written next to them, not settings. A setting is worth adding once somebody
  has a reason to change it.
- Scanning is periodic work a container does forever, so it must be cheap: the
  walk reads directory entries and opens nothing, and the database is written in
  batches with no transaction held across the filesystem.
