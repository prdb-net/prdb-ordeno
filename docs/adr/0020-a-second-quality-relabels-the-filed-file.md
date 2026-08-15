# 0020. A second quality relabels the file that is already filed

- **Status:** Accepted
- **Date:** 2026-08-15

## Context

[ADR 0003](0003-duplicates-are-skipped-not-deleted.md) keeps both qualities of a
scene, and section 6 of [`docs/jellyfin-layout.md`](../jellyfin-layout.md)
measured what Jellyfin needs in order to show them as one entry with two
versions: both files in the same scene directory, each named after that
directory plus ` - [<label>]`.

The same section measured the case that actually happens on a running
installation, and left the answer open on purpose. A scene is filed once, as a
plain `<scene>.mkv`, because at that point there is only one of it. Months later
a second quality turns up. Jellyfin accepts the unlabelled file as a version
alongside a labelled newcomer — **no rename is required** — but it names each
source by whatever follows the directory name, so the version list reads

```
Example Studio - 2025-11-16 - Mixed Labels Plain First, [2160p]
```

— one entry showing its entire file name and saying nothing about its quality,
sorted first whatever it contains. Where both files are labelled the same list
reads `[2160p], [1080p]`, highest first.

Both work. The choice is between leaving a filed file untouched and accepting a
version list that does not say which is which, or renaming a file the user
already considers filed in order to get one that does.
[#17](https://github.com/prdb-net/prdb-ordeno/issues/17) cannot be written
without picking one.

## Decision

**When a second quality arrives, the file already filed is renamed to carry its
own label, and the newcomer is written next to it.** Both end up bracketed, and
the version list reads `[2160p], [1080p]`.

The first copy of a scene is still filed unlabelled. A label on a file that is
the only one of its kind answers a question nobody has asked, and section 3's
plain shape stays the ordinary case in a library where most scenes are held
once.

Three things make this rename affordable here and nowhere else, and they are the
whole of the argument:

- **It is a rename within one directory.** Both names are inside the scene
  directory, so they are on one filesystem by construction. It is the fast path
  of [ADR 0002](0002-files-are-moved-not-copied.md) — instant, atomic, and
  unable to half-happen. No bytes are read, copied or deleted; there is no
  moment at which the file exists twice or not at all.
- **The new name is derived from the directory the file already sits in.** The
  invariant section 6 rests on — a video file name begins with its directory
  name, character for character — holds before and after, and the tool never
  composes the two names side by side.
- **The tool relabels only what it filed itself.** It knows the label because it
  recorded it when it moved the file there. A scene directory it has no record
  of is not a second quality; it is the collision case
  [#20](https://github.com/prdb-net/prdb-ordeno/issues/20) already answers, and
  it is filed around rather than written into.

The order is fixed: **relabel first, then move the newcomer in.** If the second
step fails or the container stops between them, the library holds one correctly
labelled file — a valid single-version entry, and a tidier one than before.
Doing it the other way round would leave two files in one directory of which
only one is labelled, which is the state this decision exists to avoid.

**Quality is compared as the label, never as the dimensions.** Two 1080p encodes
are the same quality and the second is not filed (ADR 0003), even where one is
1920×1080 and the other 1918×1080. Comparing exact dimensions would file both
and then want to give them the same `[1080p]` name.

**A file whose quality cannot be read is not filed.** Reading it means asking
ffprobe about a container header, which is the cheap end of what the image
already ships ffmpeg for — nothing like the twenty-five frame decode a
perceptual hash costs, so it sits in the filing path without a backlog behind
it. When it fails, neither the skip nor the label can be decided, and the hard
rule in `AGENTS.md` says nothing is written on a partial answer. The file stays
where it is and says why. It is also a strong hint that the file is not
playable, which is worth putting in front of the user rather than filing into a
library.

## Alternatives considered

- **Leave the filed file untouched.** The safer shape, and the one the layout
  research leaned towards: filing never writes against content the user
  considers finished. Rejected because the cost is not really cosmetic. The
  unlabelled entry sorts first whatever it holds, and shows a full file name
  where the other shows `[2160p]`, so the one screen where the user picks
  between two versions is the screen that stops telling them which is which —
  in exactly the library that has been running longest.
- **Label every file from the moment it is filed.** Never renames anything, and
  the version list is always tidy. Rejected for two reasons: it puts a
  resolution into every name in a library where most scenes are only ever held
  once, and it does not actually remove the mixed case — a file filed by an
  earlier version of the tool, or by hand, is unlabelled all the same, and the
  rename has to exist for it anyway.
- **Rename on the next pass rather than while filing.** A background tidier that
  relabels lone files. Rejected: it is a bulk operation over files the user
  considers sorted, which `VISION.md` names as the most dangerous thing this
  tool does. Doing it as part of a filing somebody asked for keeps it attached
  to the one file it is about.

## Consequences

- Filing writes against already-filed content. It is one rename inside one
  directory, reported in the preview before it happens
  ([ADR 0022](0022-filing-happens-when-it-is-asked-for.md)), and the operation
  log ([#19](https://github.com/prdb-net/prdb-ordeno/issues/19)) has to record
  it as its own step when it exists — an undo that returns the newcomer to the
  download directory and leaves the relabelled file under its new name would be
  a half undo.
- The tool has to remember which scene it filed where and at what quality. That
  record is the smallest thing filing needs in order to work at all, since a
  filesystem cannot say whose a directory is, and it is what the operation log
  will grow out of rather than a second copy of it.
- Every filed video has a known quality, because one that could not be read was
  not filed. Nothing downstream has to carry an "unknown" case.
- The library can hold `<scene>.mkv` and `<scene> - [1080p].mkv` shapes side by
  side, and does for as long as a scene is held once. Nothing may assume either.
