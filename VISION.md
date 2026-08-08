# Vision

`prdb-ordeno` turns a folder of freshly downloaded, arbitrarily named video files
into a library that Jellyfin, Plex or Emby can present properly — sorted into a
predictable structure, with metadata and artwork alongside it — and then keeps
doing that on its own.

It is a self-hosted tool. It runs as a container next to the media server it
feeds, and it is set up once and left alone.

## The problem

Downloads land in a directory as whatever the release was called. A media server
pointed at that directory shows a list of filenames, because that is all there
is: no title, no site, no date, no cast, no artwork. The alternative today is to
rename and file everything by hand, which is tedious enough that most libraries
stay unsorted.

prdb already knows what these files are. What is missing is the piece that sits
between a download directory and a media server and does the filing.

## Who it is for

Someone who runs their own media server and their own storage — typically on a
NAS (TrueNAS, Unraid, Synology and the like) or a home server — and who reaches
for Docker Compose rather than an installer.

That audience shapes the whole tool. Storage is mounted into the container, not
discovered; configuration survives a container rebuild; the interface is a web
UI on a port, because on a NAS there is often no desktop to run anything else
on. It is a single-user tool: no accounts, no roles, no tenants.

## What it does

The core loop, running continuously:

1. **Watch** one or more source directories, wherever downloads accumulate.
2. **Identify** each video against prdb.
3. **Move** it into a target structure following a layout the user picked, based
   on which media server they use.
4. **Enrich** it with the sidecar files that server reads — metadata, and
   optionally artwork from prdb.
5. **Repeat**, without being asked again.

Anything it cannot identify confidently is not a failure state to be hidden. It
surfaces in the UI as work waiting for a decision, and the user resolves it
there.

A watched directory is full of files that are not ready: a download still being
written, a temporary name, an archive mid-extraction. Only a file that is
demonstrably finished is a candidate, and the tool would rather wait another
cycle than act on a growing file. Watching is also not a solved problem on this
audience's hardware — filesystem notifications are unreliable across SMB and
NFS, which is exactly where a NAS user's media sits, so periodic scanning is the
normal case rather than a fallback.

## Identification

Recognition is a ladder, not a single lookup, and the tool is expected to climb
only as far as the evidence takes it:

- **File hash.** prdb holds `osHash` and `pHash` values with file sizes, so a
  file that someone else has already identified is identified here too, whatever
  it has been renamed to. This is the strongest signal and it is tried first.
- **Release name.** prdb knows the scene release names (`preNames`) belonging to
  a video, so a file still carrying its original name resolves without a hash
  match.
- **Site.** Failing both, the site is often still readable from the filename.
  That is enough to file the video under the right site even though the specific
  scene is unknown — which is a much better outcome than leaving it in the
  download directory, and it is the case that most obviously separates this tool
  from a naive hash matcher.
- **The user.** Everything left over goes to a review queue. Assigning a video
  by hand must be quick, because this is the queue that decides whether someone
  keeps using the tool after the first week.

A partial or failed lookup never becomes a guess. The tool would rather leave a
file where it is than file it wrongly, and an API error stops work rather than
degrading it.

## The target layout

The structure is not freely composed. The tool ships a small set of layouts,
one per supported media server, because the point is that the library works in
Jellyfin, Plex or Emby — and each of those has its own conventions for where
files go and what the sidecars are called. Choosing a layout is therefore
answering "what do you watch this in?", not filling in a pattern language.

Illustrative, not a specification:

```
Library/
  Site Name/
    Site Name - 2025-11-03 - Scene Title/
      Site Name - 2025-11-03 - Scene Title.mkv
      Site Name - 2025-11-03 - Scene Title.nfo
      poster.jpg
      fanart.jpg
```

Metadata is written as sidecar files in the format the chosen server reads —
`.nfo` for Jellyfin, and the equivalent for the others. Artwork from prdb is
optional and off unless enabled, since it costs bandwidth and disk that not
everyone wants to spend.

Actual layouts, filename shapes and sidecar contents are a design decision to be
made against each server's documented behaviour, and validated against a real
library rather than a reading of the docs.

## Files are moved, not copied

The download directory is meant to end up empty. Tidiness on disk is part of
what the user came for, and a tool that leaves a second copy behind has solved
the media server's problem while making the storage problem worse.

This is why the documentation has to be honest about source and target sharing a
filesystem: a move within one filesystem is instant and cannot half-happen,
while a move across two is a copy, a verification and a delete — slow, and the
only place where a file can be lost to a crash. Both must work, but the user
should be able to choose the fast one knowingly rather than discover the slow one
by watching a progress bar.

## Duplicates

Two files can be the same scene. Whether that is a problem depends on what
differs between them, so the default distinguishes:

- **Different quality** — both are kept. Someone who has the 1080p and the 2160p
  version usually has a reason, and quietly discarding one is not a decision the
  tool should make on their behalf.
- **Same scene, same quality** — the library does not hold it twice. The second
  file is not filed, and it is not deleted either: it is left where it is and
  reported, because a redundant copy is a reason to skip an import, never a
  reason to destroy data on a default setting.

The user can change this, in both directions. Note that comparing quality means
reading it out of the file itself — prdb identifies the scene, not the encode a
particular person happens to have — and that "same scene" is only knowable for
files that were actually identified. Two unidentified files are never assumed to
be duplicates of each other.

## The library is not written once

Everything above describes a file arriving. The tool then runs for years, and
during those years the library itself changes underneath it:

- The user switches media server, or picks a different layout. Their existing
  library was filed under the old one.
- prdb corrects a title, a date, or a cast entry. The `.nfo` written last spring
  still says the old thing.
- A scene the user resolved by hand turns out to have been resolved wrongly.

None of these are edge cases; over a long enough run all three are certain. So
re-filing an existing library and refreshing existing metadata are part of the
tool rather than something to bolt on later — and both are bulk operations over
files the user already considers sorted, which makes them the most dangerous
thing it does. How far each goes, and how much is automatic versus confirmed,
is still open.

## Every operation is reversible

The counterpart to treating files as irreplaceable: the tool keeps a log of what
it moved, from where, to where, and why it believed that was right. The log is
what the UI shows, what a bug report quotes, and what an undo reads.

Undo matters most exactly where the tool is most useful — the unattended run
that filed two hundred files overnight under a rule that turned out to be wrong.
Without a way back, the only safe way to use such a tool is to not let it run
unattended, which is the whole point of it.

## Contributing back to prdb

The user may optionally let the tool report what it has found back to prdb —
marking videos as fulfilled, so their wanted list reflects reality. This is
**off by default and opt-in**, and it stays that way. A tool that quietly tells
a remote service what is on someone's disk is not a tool anyone should install,
however useful the aggregate data would be.

## Principles

**Files are irreplaceable.** The one hard rule in `AGENTS.md` governs everything
here: destructive operations are opt-in, cross-filesystem moves are
copy-verify-delete, nothing is written on a failed lookup. A web UI raises the
stakes rather than lowering them, because a button is easier to press than a
command is to type. Before anything moves, the user must be able to see exactly
what would happen.

**Set up once.** The value is in the unattended running. Onboarding — API key,
source directories, target directory, layout — should be a short guided path
that ends with the user watching the first batch get filed, and after that the
tool should not need attention. A tool that needs weekly babysitting has failed
at its actual job.

**Usability is a feature, not polish.** The review queue, the onboarding, and
the "what is it about to do" view are the product. Everything else is
plumbing.

**Docker is the supported way to run it.** Not one deployment option among
several — the way. Source and target storage arrive as mounts, and the
documentation has to teach that properly, including the parts people get wrong:
`PUID`/`PGID` and ownership, NAS shares, and why the source and target should
sit on the same filesystem if you do not want every file copied instead of
moved.

**Reachable, but not open.** Even on a LAN, the UI is behind a password. One
password, no username, no email — an email address may be added later, and only
for alerting. Someone who exposes this to the internet has made their own
choice, but the default must not be an open door.

**prdb is the only metadata source.** It is reached through `prdb-sdk`, never a
hand-rolled HTTP client. The tool does not scrape and does not maintain a
metadata corpus of its own; a wrong title is a prdb problem with a prdb fix,
which keeps the responsibility where the correction can actually be made.

## What it is not

- Not a downloader, and not an indexer client. It starts where the download
  finished.
- Not a media server or a player. It produces a library; something else serves
  it.
- Not a multi-user or hosted application.
- Not a general-purpose renamer. The layouts exist to satisfy specific media
  servers, and a pattern language for its own sake would only make the common
  case harder.
- Not a metadata editor. Corrections belong upstream in prdb.

## Prerequisites for the user

- A prdb account with an API key — the tool cannot identify anything without
  one, and this needs to be said plainly before someone installs it, not
  discovered at first run.
- Docker, and storage that can be mounted into a container.

## The first release, and what comes after

The first release has to be the whole loop and nothing else: sources and a
target configured through onboarding, identification, filing, sidecar metadata,
a review queue for what could not be identified, and the operation log with its
undo. A version of this that identifies beautifully but leaves the user without
a way back is not a smaller release, it is an unusable one.

Deliberately after that, not before:

- **Notifications.** An unattended tool has to be able to say something went
  wrong without the user thinking to go and look. This is the first thing on the
  list once the loop works, and it is why an email address may appear in the
  configuration later — for alerting, and for nothing else.
- **Artwork**, if it turns out to be more than a small addition to the sidecar
  work.

## Open questions

- The implementation language, and with it which `prdb-sdk` client is used.
  These are one decision, not two — see `AGENTS.md`.
- The exact layouts per media server, and how far artwork support goes.
- How much of re-filing and metadata refresh happens on its own, and how much
  the user confirms first.
- How the review queue behaves at scale: a first run over an existing library of
  thousands of files is a different problem from the steady state of a handful
  of new downloads a day, and the design has to survive both.
