# The Jellyfin layout

This is the layout the first release files into, and the evidence behind it.
[ADR 0008](adr/0008-the-first-release-targets-jellyfin-only.md) makes Jellyfin
the only media server the first release targets, and `VISION.md` asks for the
layout to be validated against a real library rather than read out of the
documentation. This document is the result of doing that.

Every answer below names what was put on disk and what Jellyfin then reported.
Where the documentation and the observed behaviour disagree, the observation
wins and the disagreement is recorded, because that is the whole reason for
running the exercise against a server.

## How this was measured

- **Jellyfin 10.11.11**, image `jellyfin/jellyfin:10.11.11`, started from
  `docs/jellyfin-probe/docker-compose.yml`.
- A fixture library written by `docs/jellyfin-probe/make-fixtures.sh` — 18 groups
  of cases, 59 directories, 169 files — mounted read-only, so that nothing
  Jellyfin did could change what the observation was made against.
- Two libraries over that fixture root, one declared **Movies** and one declared
  **Home Videos**, both with remote metadata and image providers switched off,
  so that anything an item ended up carrying demonstrably came from the sidecar
  or from the file rather than from the internet.
- Readings taken through Jellyfin's own API by `docs/jellyfin-probe/probe.sh`;
  the raw result is committed as `docs/jellyfin-probe/observation.json`.
- The storage limits in the last section were measured separately by
  `docs/jellyfin-probe/probe-path.sh`, against a local ext4 filesystem as a
  control and against an SMB 3.1.1 share on a NAS.

To repeat the whole thing:

```
cd docs/jellyfin-probe
export ORDENO_FIXTURES=/var/tmp/ordeno-jellyfin-fixtures
export ORDENO_PROBE_UID=$(id -u) ORDENO_PROBE_GID=$(id -g)
./make-fixtures.sh "$ORDENO_FIXTURES"
mkdir -p state/config state/cache && docker compose up -d
./probe.sh setup && ./probe.sh scan && ./probe.sh dump observation.json
./probe-refresh.sh
./probe-itemid.sh
```

Keep the fixtures outside the repository — they are a hundred generated files
the generator can recreate at any time. `probe-refresh.sh` and `probe-itemid.sh`
edit one of them, so a run leaves the fixture set dirty; regenerate before
repeating. Both of them also wait out a one-minute tolerance window several
times over, deliberately: they take minutes, and the waiting is the measurement.

The generator clears the fixture root's contents rather than the root itself,
and it matters. Removing the directory that is bind mounted into the running
container leaves that mount pointing at a deleted inode: the container keeps
reading a frozen copy of the old tree while every subsequent edit lands
somewhere it cannot see. That failure is silent, and it looks precisely like
Jellyfin refusing to notice changed sidecars — which is how it was found.

## The layout

```
<target>/
  <Site>/
    <Site> - <yyyy-MM-dd> - <Title>/
      <Site> - <yyyy-MM-dd> - <Title>.mkv
      movie.nfo
      poster.jpg      (only when artwork is enabled)
      fanart.jpg      (only when artwork is enabled)
```

and, where a second quality of the same scene is kept
([ADR 0003](adr/0003-duplicates-are-skipped-not-deleted.md)):

```
      <Site> - <yyyy-MM-dd> - <Title> - [1080p].mkv
      <Site> - <yyyy-MM-dd> - <Title> - [2160p].mkv
```

The scene directory name is the unit everything else is derived from: the video
file repeats it exactly, and the version label is appended to that repetition.

## 1. Library type: Movies

The library is declared as **Movies**. It is the only one of the candidate types
that reads the sidecar this layout depends on.

The same directory — one video file and one `movie.nfo` — was offered to a
Movies library and to a Home Videos library in the same server, with the same
options:

| Library type | Item type | Name Jellyfin showed | Premiere date |
| --- | --- | --- | --- |
| Movies | `Movie` | `Movie Library Read movie dot nfo` | `2025-11-21` |
| Home Videos | `Video` | `Example Studio - 2025-11-20 - Movie Nfo Only` | none |

The Home Videos library ignored `movie.nfo` completely and fell back to the file
name. It reads a per-file `<video file name>.nfo` instead — a separate fixture
carrying one of those was read correctly — but that sidecar name is unusable
here for the reason in section 6: it collides with the multi-version rule.

Mixed libraries are not a candidate; Jellyfin's own documentation discourages
them, and nothing here needed them.

## 2. Directory structure: one directory per scene

**One directory per scene. A flat directory per site does not work** — not as a
matter of tidiness, but because Jellyfin merges the scenes into a single entry.

The fixture put two unrelated scenes loose in one site directory, each with its
own correctly named sidecar:

```
Flat Site Directory/
  Flat Site Directory - 2025-11-09 - Flat Scene One.mkv
  Flat Site Directory - 2025-11-09 - Flat Scene One.nfo
  Flat Site Directory - 2025-11-10 - Flat Scene Two.mkv
  Flat Site Directory - 2025-11-10 - Flat Scene Two.nfo
```

Jellyfin produced **one** `Movie` item with **two media sources**. Both per-file
sidecars were ignored. Two scenes became one entry, and one of them became
unreachable as a thing in its own right.

The cause is Jellyfin's multi-version grouping. Files in one directory are
treated as versions of a single title when every file name begins with the
directory name and the remainder starts with `-`, `_`, `.` or a bracketed label.
`Flat Site Directory - 2025-11-09 - Flat Scene One` begins with
`Flat Site Directory` and the remainder starts with `-`, so it qualifies. The
naming convention this tool wants and the grouping rule Jellyfin applies are the
same shape, which makes the flat layout actively dangerous rather than merely
suboptimal.

A directory holding videos that do **not** all qualify is left alone — a second
fixture with one matching and one non-matching file produced two separate
movies — but it also leaves a stray `Folder` entry in the library alongside
them. One directory per scene avoids both outcomes.

### The site directory above it is safe

The layout puts the scene directories under a directory per site, which is a
second level Jellyfin was not asked about above. A separate fixture placed two
scenes and one two-version scene under `Nested Site/`:

- both scenes resolved as separate movies, with their own titles and dates from
  their own sidecars;
- the two-version case still merged into one movie with two media sources, so
  the grouping rule works a level down as well;
- the site directory itself appeared as a `Folder` item in the library.

That last point is the only cost. The scenes below it are not direct children of
the library root — 49 movies sat at the top level of the fixture library and 56
were found recursively — so anything browsing strictly by folder has to descend
one level. Jellyfin's Movies view queries recursively and shows all of them, so
this is not visible in normal use, and the site directory earns its place by
making the library navigable on disk, which is half of what the user came for.

## 3. Filename shape

The video file is named exactly like its directory, plus the extension.

**Jellyfin parses nothing useful out of the name this tool produces.** Given
`Example Studio - 2025-11-06 - No Sidecar At All.mkv` and no sidecar, the item
was called `Example Studio - 2025-11-06 - No Sidecar At All` — the whole file
name verbatim — with no premiere date and no production year. The date in the
name is not read as a date. The documented `Name (Year)` convention is the only
shape the parser recognises, and this layout deliberately does not use it,
because the sidecar supplies the same information unambiguously.

Where the file name and the sidecar disagree, **the sidecar wins**: a file named
`... - 2025-11-05 - Filename Says This.mkv` next to a `movie.nfo` giving the
title `Sidecar Says Something Else` and `<premiered>2019-01-31</premiered>`
produced exactly that title and that date, with production year 2019.

The file name therefore exists for humans and for the version grouping in
section 6, not for metadata. That is a useful property: it means the displayed
library does not degrade when a title contains something the file name had to
have escaped.

## 4. The sidecar

**`movie.nfo`, in the scene directory, root element `<movie>`.**

When both `movie.nfo` and `<video file name>.nfo` are present, **`movie.nfo`
wins** — the fixture that disagreed on purpose displayed the title from
`movie.nfo`.

These fields were written and came back:

| Element | Where it appeared |
| --- | --- |
| `<title>` | item name |
| `<originaltitle>` | original title |
| `<sorttitle>` | sort name |
| `<plot>` | overview |
| `<premiered>` | premiere date, and production year when `<year>` is absent |
| `<studio>` | studios, and a browsable studio entry |
| `<genre>` | genres |
| `<tag>` | tags |
| `<uniqueid type="prdb">` | provider id under the key `prdb` |
| `<actor>` | people — see below |

### The release date has exactly one accepted format

`<premiered>` is parsed against a single configured format, `yyyy-MM-dd`, and
anything else is discarded without a complaint. Five fixtures, all claiming
7 November 2025:

| `<premiered>` | Premiere date | Production year |
| --- | --- | --- |
| `2025-11-07` | 2025-11-07 | 2025 |
| `07/11/2025` | none | none |
| `07.11.2025` | none | none |
| `2025-11-07T00:00:00` | none | none |
| `07 November 2025` | none | none |

The ISO form with a time is worth singling out: it is a valid ISO 8601
timestamp, it is what most date libraries produce by default, and Jellyfin drops
it. The writer must emit a bare `yyyy-MM-dd`.

Note also that the format is a server setting rather than a constant. This is
the value a default installation has, and a user who has changed it will get no
dates from a correctly written sidecar.

### A performer becomes a person only in one shape

Seven `<actor>` nodes in one sidecar, and what each became:

| Written | Result |
| --- | --- |
| `<name>` + `<role>` + `<type>Actor</type>` + `<order>` + `<thumb>` | person, type Actor, role kept, image tag set |
| `<name>` alone | person, type Actor, empty role |
| `<actor>Bare Text</actor>` — no `<name>` child | **dropped** |
| `<name>` + `<type>Director</type>` | person, type Director |
| `<name>` + `<type>Performer</type>` | person, type **Unknown** |
| `<name>` + `<order>9</order>` | person, type Actor |
| empty `<name>` | **dropped** |

Two rules follow. A performer must be written as an `<actor>` element with a
`<name>` child — text directly inside `<actor>` is silently discarded, which is
the shape a naive writer is most likely to produce. And `<type>` must be a value
Jellyfin knows: `Performer` is not one, and rather than being rejected or
defaulted it produces a person of type `Unknown`. **Write `<type>Actor</type>`**,
whatever prdb calls the role.

### The sidecar is XML, and titles are arbitrary text

This was found by getting it wrong. The first version of the fixture generator
wrote titles into the sidecar unescaped, and the three cases whose titles
contained `&`, `<` or `>` produced an unparseable document. Jellyfin reported no
error: it simply used none of the sidecar and fell back to the file name, which
looks exactly like a metadata lookup that returned nothing. With the same titles
escaped, all three read correctly.

`&`, `<` and `>` at minimum have to be escaped on the way in. A scene title
containing an ampersand is not an edge case.

## 5. Artwork

Artwork is optional and off by default, so the question is which names are worth
writing. One image per directory, sixteen directories, to see which file lands
in which slot without two candidates competing:

| File name | Becomes |
| --- | --- |
| `poster.jpg`, `folder.jpg`, `cover.jpg`, `default.jpg`, `movie.jpg` | Primary |
| `fanart.jpg`, `backdrop.jpg`, `background.jpg`, `art.jpg` | Backdrop |
| `logo.png`, `clearlogo.png` | Logo |
| `clearart.png` | Art |
| `disc.jpg` | Disc |
| `banner.jpg` | Banner |
| `thumb.jpg`, `landscape.jpg` | Thumb |
| `<video file name>-poster.jpg` | Primary |

Two of these contradict what the names suggest. **`art.jpg` becomes a backdrop,
not the Art image** — `clearart.png` is what fills the Art slot. And
`landscape.jpg` and `thumb.jpg` compete for the same slot rather than being two
different things, so writing both means one of them is wasted.

The suffix form has a defect of its own. A directory holding one
`<video file name>-fanart.jpg` produced **two** backdrops, with identical image
tags and byte-for-byte identical content — the same file registered twice. A
plain `fanart.jpg` produced one. Nothing breaks, but it is a reason to prefer
the plain names beyond their being shorter.

A directory with no artwork at all produced a perfectly good item with no
images and no errors. Absence costs nothing.

**Write `poster.jpg` and `fanart.jpg`, and only when the user has enabled
artwork.** Every other name is either a duplicate of those two slots or fills a
slot this content has no source for.

## 6. Two versions of the same scene

One directory. Both files begin with the directory name, then ` - `, then a
bracketed label:

```
Example Studio - 2025-11-11 - Two Versions Bracketed/
  Example Studio - 2025-11-11 - Two Versions Bracketed - [1080p].mkv
  Example Studio - 2025-11-11 - Two Versions Bracketed - [2160p].mkv
  movie.nfo
```

Jellyfin produced one `Movie` named from `movie.nfo`, carrying two media sources
labelled `[2160p]` and `[1080p]`, the higher resolution first, with the correct
widths read from the files. This is the documented behaviour and it holds.

**The bracket form is not decoration.** The same two files named
`... Two Versions Bare 1080p.mkv` and `... Two Versions Bare 2160p.mkv` were not
merged, and the outcome is worse than not merging: the resolution token is
stripped when the display name is derived, so the library showed **two separate
items with identical names** and no way to tell them apart. A writer that
appends the quality without the ` - [...]` shape produces a library that looks
broken.

### The second quality arrives later

A scene is filed once, as a plain `<scene>.mkv` with no label, because at that
point there is only one of it. Months later a second quality turns up. The
question that decides how much work that is: does the unlabelled file have to be
renamed first, or is it accepted as a version alongside the labelled newcomer?

**It is accepted, and no rename is needed.** Both orderings were tried — the
unlabelled file carrying the lower quality, and carrying the higher — and both
produced one movie with two media sources:

| Directory contains | Result |
| --- | --- |
| `<scene>.mkv` (1080) + `<scene> - [2160p].mkv` | one item, two versions |
| `<scene>.mkv` (2160) + `<scene> - [1080p].mkv` | one item, two versions |

In both cases the **unlabelled file became the primary version**, regardless of
which resolution it held.

There is a cosmetic cost, and it is worth knowing before choosing. The version
list shows each source by the part of the name that follows the directory name,
so a labelled file appears as `[2160p]` while the unlabelled one appears as its
entire file name — and says nothing about its quality. Where both files are
labelled the list reads `[2160p], [1080p]`, highest first; where one is not, it
reads `Example Studio - 2025-11-16 - Mixed Labels Plain First, [2160p]`, with the
unlabelled one first whatever it contains.

So the choice is between leaving a filed file untouched and a version list that
does not say which is which, or renaming a file the user already considers filed
in order to get a tidy one. That is a decision for the filing path rather than a
property of Jellyfin, and both options work.

## 7. Refresh

A plain library scan re-reads a `movie.nfo` that changed on disk, and **the only
thing that decides whether it does is how long after Jellyfin last saved that
item the edit landed.**

Two things could plausibly have mattered: how the edit reached the disk, and
when. Rewriting a file in place leaves the containing directory's timestamp
untouched, while writing a temporary file and renaming it over the original
moves it — a scan that watched directories would notice one and miss the other.
All four combinations:

| Edit | Delivered by | Result |
| --- | --- | --- |
| within a minute of the last save | rewrite in place | ignored |
| after waiting past the minute | rewrite in place | picked up |
| within a minute of the last save | write and rename | ignored |
| after waiting past the minute | write and rename | picked up |
| targeted refresh with `replaceAllMetadata=true` | — | picked up immediately |

The write method makes no difference at all. Jellyfin compares the sidecar's
modification time against the moment it last saved that item and ignores
anything within one minute of it, deliberately, so that its own writes do not
read as external changes. An edit landing inside that window is invisible until
something else triggers a refresh — and a tool that writes a sidecar and
immediately asks for a scan lands inside it every time.

So: a scheduled scan is enough for the case `VISION.md` cares about, a sidecar
refreshed years later. For a refresh the tool performs itself and wants to see
take effect, it must either stay out of that one-minute window or ask for the
item directly:

```
POST /Items/{itemId}/Refresh?metadataRefreshMode=FullRefresh&replaceAllMetadata=true
```

That worked regardless of timing. Note that it requires knowing the Jellyfin
item id, which means talking to Jellyfin's API — something this tool does not
otherwise do, and should not take on lightly.

## 8. Characters and length

**Jellyfin is not the constraint here; the storage is.**

Sixteen directories, each with a different character class in its name, each
with a poster. All sixteen resolved to items with the sidecar title, and all
sixteen served their poster over HTTP with status 200 — including `:`, `?`, `*`,
`|`, `<`, `>`, `"`, `&`, `#`, `%`, `+`, `'`, `[]`, accented Latin, CJK, an em
dash and an emoji. Nothing needed escaping for Jellyfin's sake.

The limits come from the filesystem the library sits on. Measured against an
SMB 3.1.1 share on a NAS, mounted with `mapposix`, with a local ext4 filesystem
as the control:

| | ext4 | the SMB share |
| --- | --- | --- |
| `\` in a name | accepted | **rejected**, `EINVAL` |
| `" * : < > ? \|` | accepted, stored as written | accepted, **not stored as written** |
| trailing space or period | accepted | accepted |
| component length limit | 255 bytes | 255 bytes |
| total path length | no practical limit | no practical limit (over 1200 characters) |

The second row is the trap. Those characters appear to survive: `ls` shows them
unchanged. But writing the raw private use codepoints and reading the directory
back shows the mapping directly — U+F020 comes back as `"`, U+F022 as `:`,
U+F025 as `?`, U+F027 as `|`, U+F028 as a space, U+F029 as a period. The client
is translating in both directions, and what the server stores is the private use
codepoint. Another client, the NAS's own file manager, a backup tool, or the
same share mounted without `mapposix`, sees something else — and a mount without
`mapposix` rejects those characters outright.

The length limit is counted in **bytes, not characters**: 85 CJK characters fit
in a component and 86 do not, on both filesystems. A title budget expressed in
characters will overflow on any library that is not pure ASCII.

What follows for the writer:

- **Escape the Windows-reserved set — `< > : " / \ | ? *` — regardless of what
  a given mount happens to tolerate.** The tool cannot see which mount options
  its target was mounted with, and the failure mode of guessing wrong is a
  filed library that another machine cannot read.
- **Budget 255 bytes per path component, not 255 characters**, and measure the
  encoded length.
- Total path length needs no budget on these filesystems, but the target may be
  re-shared to a Windows client, where 260 characters is the traditional limit.
  Worth documenting for the user rather than enforcing.

## 9. From a path to an item

Everything above is about files. This section is about the two things that need
the server's cooperation: making a change the tool has just written appear at
once (section 7), and finding out whether the server will read the dates the tool
writes (section 4). Both mean talking to the API, and what that costs is mostly
in the route from what the tool knows — a path it just wrote to — to what the API
wants. Measured by `docs/jellyfin-probe/probe-itemid.sh`, against the same server
and fixtures. The decision this fed is
[ADR 0018](adr/0018-the-jellyfin-connection-is-optional.md).

### An item is found by enumerating, and by nothing else

An item's `Path` is the **video file**, not the scene directory —
`/fixtures/movies/<scene>/<scene>.mkv`, the same value as its single
`MediaSources[0].Path`. Three ways to get from that path to the item:

| Route | Result |
| --- | --- |
| `GET /Items?recursive=true&fields=Path`, matched here | works — 58 movies, 43 KB of JSON |
| `GET /Items?…&path=<the exact path>` | **the whole library**, all 58 items |
| `GET /Items?…&searchTerm=<the scene directory name>` | 0 items |

The middle row is the trap. The parameter is accepted, the response is a 200,
and the filter is not applied — a caller that passes a path and takes
`.Items[0]` refreshes an arbitrary item. The last row fails for a reason worth
knowing: the name Jellyfin indexes comes from the sidecar, so the directory name
the tool built is not something the server can be asked about.

### The path the tool knows is not the path the server knows

Both sides run in containers with their own mounts. In the probe the same
directory is `/var/tmp/ordeno-jellyfin-fixtures/movies/<scene>` here and
`/fixtures/movies/<scene>` there, and neither configuration mentions the other.

`GET /Library/VirtualFolders` reports its `Locations` as `/fixtures/movies`, and
`GET /Library/PhysicalPaths` the same plus `/config/data/playlists`. That is
enough to see *that* the two disagree and not enough to translate, because
neither names the host side.

Matching the **tail** of the path does both. The site directory, the scene
directory and the file name are identical on both sides — only the mount prefix
differs — so matching on `…/<scene>/<scene>.mkv` found the item, and subtracting
the tail from what came back yields the prefix the server uses. The substitution
can be discovered rather than configured.

### Reporting a path instead of refreshing an item

`POST /Library/Media/Updated` takes a path rather than an item id, which would
make the lookup above unnecessary. It half works, and the half that does not is
the half this tool needs.

Every case below waited past the one-minute tolerance from section 7 first —
except the last, which deliberately did not — and nothing triggered a scan
afterwards, so a change that appeared was the report's doing. The one case that
did produce a change was then measured again with a control: the same wait with
no report in it, to prove the item was not being refreshed by something else. The
rows that report nothing need no such control, because a stray refresh can only
manufacture a hit, never a miss. Real-time monitoring was tried on and off, and
made no difference to any row.

| Reported path | Result |
| --- | --- |
| the video file, as the server sees it | **picked up** |
| the scene directory | ignored |
| the `movie.nfo` | ignored |
| the video file, as *this side* sees it | ignored, and the call still answers 204 |
| the video file, edit inside the tolerance window | ignored |

So it is a way to say "this file changed, read it again" without scanning the
library — and it obeys exactly the same tolerance a scan does, which means it
cannot do the one thing a targeted refresh is for. It also has to be given the
path Jellyfin knows, which is the mapping the tool has to derive anyway, and a
wrong one is answered with a 204 and silence.

That last property is the argument for the item id rather than against it.
Finding the id means the tool matched something the server actually has; the id
*is* the receipt for the path substitution being right. A path report has none.

The control in that table was added after the fact. The first version of this
measurement ran the cases back to back with no control and produced one hit that
did not reproduce: the library monitor collects what it is told and acts on it
later, so a report from the previous case lands in the middle of the next one and
reads as its result.

### An API key is enough, and no user account

A key created in Jellyfin's dashboard (`POST /Auth/Keys`) reached everything this
needs:

| Endpoint | Status |
| --- | --- |
| `GET /System/Info` | 200 |
| `GET /Library/VirtualFolders` | 200 |
| `GET /Items?recursive=true&fields=Path` — no user id | 200, all 58 movies |
| `POST /Items/{id}/Refresh` | 204 |
| `POST /Library/Media/Updated` | 204 |
| `GET /System/Configuration/xbmcmetadata` | 200 |

`GET /Items` normally takes a `userId`; with an API key it answers without one.
So a connection needs a URL and a key, and no user name, password or user id.

### The release date format can be read back

The setting section 4 measured lives at `GET /System/Configuration/xbmcmetadata`,
and a default installation answers:

```json
{
  "ReleaseDateFormat": "yyyy-MM-dd",
  "SaveImagePathsInNfo": true,
  "EnablePathSubstitution": true,
  "EnableExtraThumbsDuplication": false
}
```

The one setting that silently discards every date the tool writes is therefore
one `GET` away from being visible.

## Where the documentation and the server disagreed

- `art.jpg` is documented alongside the other artwork names as its own kind. It
  becomes a **backdrop**; `clearart.png` is what fills the Art slot.
- `landscape.jpg` and `thumb.jpg` are described separately but occupy the **same
  slot**.
- The documented `<video file name>-fanart.jpg` suffix form registers the same
  image as two backdrops.
- The multi-version rule is documented as a convenience for deliberately keeping
  several cuts of one film. It is better understood as a hazard: it fires on any
  directory whose file names share the directory's name as a prefix, which
  silently merged two unrelated scenes in the flat-directory fixture.
- Nothing in the documentation says that `<premiered>` is parsed against one
  exact format, that an ISO timestamp is rejected, or that the format is a
  server-side setting the user can change.
- Nothing says that an `<actor>` with an unrecognised `<type>` becomes a person
  of type `Unknown` rather than defaulting to an actor.

## What this settles, and what it does not

Settled: the directory shape, the file name shape, the sidecar name and root
element, the fields worth writing and the exact form three of them need, which
two artwork files are worth the bandwidth, how a second quality has to be named,
and what the tool has to escape and how long a component may be.

Section 9 was added later, when the API question below was taken up. It settles
what talking to the server would cost, not whether to do it; that is
[ADR 0018](adr/0018-the-jellyfin-connection-is-optional.md), which decided on an
optional connection that the filing path never depends on.

Not settled, and deliberately out of scope here:

- What a scene with no known date files as. Every fixture had one.
- Whether the site directory should carry artwork or metadata of its own. It
  resolves as a plain `Folder`, and nothing was written at that level to find
  out what it would do with it.
