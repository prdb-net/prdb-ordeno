# 0019. A missing date drops the segment, and a file with no title is not filed

- **Status:** Accepted
- **Date:** 2026-08-13

## Context

The layout in [`docs/jellyfin-layout.md`](../jellyfin-layout.md) puts the release
date in the middle of every name:

```
<Site> - <yyyy-MM-dd> - <Title>/
```

Every fixture it was measured against had a date, and the document says in as
many words that what a scene without one files as was left open.

The answers the tool actually holds are looser than that shape. In
`Recognition`, `Title` and `ReleaseDate` are both nullable, and
`RecognitionState.SiteOnly` is an answer with neither: the site rung of the
ladder identifies the site and not the scene, which `VISION.md` describes as a
result worth having rather than a failure. So there are three incomplete shapes,
not one — a video with a title and no date, a video with neither, and a file
whose site is all that is known.

[#20](https://github.com/prdb-net/prdb-ordeno/issues/20) computes the target path
and cannot be written against any of them until this is decided.

## Decision

**The date is optional in a name. The title is not.**

Where prdb knows no release date, the segment goes, separator and all:

```
<target>/<Site>/<Site> - <Title>/<Site> - <Title>.mkv
```

Nothing takes its place. Section 3 of the layout document measured that Jellyfin
parses nothing out of the name — the date in it is not read as a date, and where
the name and the sidecar disagree the sidecar wins — so a second name shape costs
nothing the server can see. The rule that does depend on the name is the version
grouping in sections 2 and 6, and that is a relation between a directory name and
the file names inside it; dropping a segment from both keeps it exactly.

The sidecar carries **no `<premiered>`** and no guessed `<year>`: the element is
absent rather than approximate. Section 4 leaves no useful middle ground. A value
in any other format is discarded silently and takes the production year with it,
so an evasive placeholder achieves nothing that absence does not — and a value
that does parse is worse, because Jellyfin believes it and the library then
states a release date the scene does not have.

Where prdb names no title — a video without one, and every `SiteOnly` answer —
**the file is not filed.** It stays where it is and goes to the review queue with
what is known about it already filled in. Jellyfin needs one directory per scene
(section 2), so filing such a file means naming a directory after the release
name it arrived under: an entry that tells the user no more than the download
directory did, while the file has been moved, an operation-log entry and an undo
have been spent on it, and the library now shows something that looks settled.
The site rung still earns its place — it is the difference between a queue entry
that already knows where the file came from and one that knows nothing — but that
is a queue that gets shorter, not a library that gets fuller.

A date that arrives on a later pass **updates the sidecar and renames nothing.**
Because the sidecar wins, the displayed library is correct as soon as it is
rewritten; only the name on disk lags. Renaming a directory the user considers
filed is re-filing, which `VISION.md` already names as the most dangerous thing
the tool does, and it belongs with that decision rather than as a side effect of
a second identification pass.

## Alternatives considered

- **A placeholder in the slot** — `0000-00-00`, `unknown`, a dash. Rejected. It
  keeps one name shape in exchange for putting something data-shaped on disk that
  is not data, and it buys no path stability: when the date arrives the path
  changes either way. The one real cost of dropping the segment is that a site's
  scenes no longer sort chronologically **in a file manager** — in Jellyfin that
  ordering comes from `<premiered>` and `<sorttitle>`, and the entries in question
  have no date to be ordered by in the first place.
- **Substituting a date the tool does have** — when the file was imported, or its
  modification time. Rejected outright: it is the one option that writes a
  well-formed `<premiered>` Jellyfin will believe, so the library ends up
  asserting a release date that came from the user's disk.
- **Filing a site-only file under its site anyway**, named after the release. This
  is what `VISION.md` said before this decision, and the sentence has been
  corrected. It reads well until the layout is taken into account: "under the
  right site" is a directory the file cannot occupy on its own.
- **Renaming as soon as a date arrives.** Not rejected on the merits, only
  deferred — it is a re-filing case, and re-filing is an open question in
  `VISION.md` with an operation log ([#19](https://github.com/prdb-net/prdb-ordeno/issues/19))
  under it. Deciding it here would settle the dangerous half of it by accident.

## Consequences

- A library can hold two name shapes. Nothing may parse a filed name and assume
  the date is there — which is safe to require, because section 3 established
  that nothing should be parsing those names at all.
- An entry filed without a date shows no premiere date and no production year.
  That is what is true about it.
- Every filed scene has a `videoId`, since a title only ever comes with one. The
  collision breaker #20 wants — appending the prdb scene id — is therefore always
  available, and it is needed more often here: without a date, two scenes from one
  site have one discriminator fewer.
- The download directory does not always end up empty. Files known only by site
  stay in it until the review queue settles them, which is a deliberate reading of
  what `VISION.md` asks for: the directory is emptied by filing, and a file nobody
  can name is not filed.
- Re-filing gains a trigger it did not have — "the date arrived later" — and until
  that exists, a library can hold an entry whose sidecar has a date and whose
  directory name does not. It displays correctly; it is only untidy on disk.
- This was decided from the measurements in sections 2, 3, 4 and 6 rather than
  from a new probe run. Nothing above asks Jellyfin for behaviour those sections
  did not already cover.
