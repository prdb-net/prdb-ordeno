# 0027. Artwork is one image, and it is written only where there is none

- **Status:** Accepted
- **Date:** 2026-08-19

## Context

[ADR 0024](0024-the-sidecar-is-written-by-filing-and-only-over-its-own.md) put
artwork out of the sidecar's scope and said why: the images are URLs on prdb's
CDN, so writing them means downloading them — bandwidth and disk somebody has to
have agreed to, a setting to agree with, and a decision about what a failed
download leaves in a directory that counts as occupied when anything is in it.
That became [#28](https://github.com/prdb-net/prdb-ordeno/issues/28).

Three things have happened since, and two of them make the feature smaller than
the issue describes.

**Only one file is worth writing.** #28 scoped `poster.jpg` *and* `fanart.jpg`,
on the strength of section 5 of [`docs/jellyfin-layout.md`](../jellyfin-layout.md).
That section was measured with a 600×900 poster, which is the shape the Primary
slot was designed for and a shape this content does not have — prdb's images are
the shape of the video. Measuring the real case inverted the recommendation: the
card in a Movies library is portrait whatever the image is and centre-crops it,
and the client derives the size it requests from the image's own aspect ratio, so
a 16:9 poster is fetched 113 pixels tall and stretched over a card three times
that. An item with **no** poster falls back to its backdrop, is cropped
identically, and gets 300 pixels of height to do it with. A landscape
`poster.jpg` is therefore worse than none.

**The download is not the hard part.** The issue assumed a CDN path needing a
base nobody published. It was wrong: `cdnPath` holds an absolute URL, scheme and
host included, ready to request. prdb also now documents that the image array
has a defined, stable order — oldest first by the time an image was added, image
id breaking ties. Both are stated in the schema as of `Prdb.Sdk` 0.6.2.

**What is left is the question none of the earlier decisions answers.** ADR 0024
tells the tool's own sidecar from somebody else's by a comment at the top of the
document, and says deleting that line is how a user takes the file back. An image
has no comment to put a marker in. That is this decision's real work, and the
reason artwork could not simply follow the sidecar's rule.

## Decision

**One file: `fanart.jpg`, and nothing else.** Section 5 has the full matrix of
sixteen names; every other one is a duplicate of a slot this fills, or a slot
this content has no source for, and `poster.jpg` is measured to make the library
look worse. One image per scene, one download, one thing that can fail.

**Off unless somebody turned it on**, which is the hard rule in `AGENTS.md`
applied to bandwidth rather than to data: spending somebody's connection and
disk is not something that happens because nobody said no. The switch lives in
the library settings — [ADR 0026](0026-an-area-may-have-sections-and-the-settings-do.md)'s
`/settings/library`, not a section of its own, because it is a property of what
filing writes rather than a thing of its own — and it is not an onboarding step.
[ADR 0009](0009-configuration-is-collected-by-onboarding.md) collects what the
tool cannot run without, and it runs without this.

**Written by filing, after the video and after the sidecar**, and at no other
time. The order is ADR 0024's, for ADR 0024's reason: anything written into a
scene directory before the move leaves a directory holding no video if the move
fails, and a directory with anything at all in it counts as occupied, so the
retry would file the scene around its own directory under a name carrying prdb's
id, for a reason nobody could see.

**Only where there is no file at that name. Nothing is ever written over.** This
is the marker question's answer, and it is that the marker is not needed: a tool
that never replaces does not have to recognise its own work. A `fanart.jpg`
already in the scene directory is left exactly as it is, whether the tool put it
there last month or the user did this morning — and the two cases want the same
thing anyway, because re-downloading an image that is already on disk is waste in
the first case and destruction in the second.

Deleting the file is how a user asks for a fresh one, and the next filing into
that scene brings it. That is the same affordance ADR 0024 gave the sidecar —
something a user can do in a file manager with no setting to find — and here it
needs no marker to make it work.

**A download that fails leaves nothing behind.** The bytes go to a dotted
temporary name in the same directory, are flushed to disk, and are renamed into
place; a failure at any point deletes the temporary file. This is exactly what
`Sidecars.Write` already does and for the same reason, one step stronger: a
half-written `fanart.jpg` is not merely a bad image, it is a file at the name
that stops the next run from writing the good one.

**The download is bounded, and what arrives is checked before it is kept.** A
size cap and a confirmation that the bytes are a JPEG. The URL is one the tool
did not compose and the response is not one it controls; a scene directory is not
a place to put whatever answered. A failure here is a failure like any other and
leaves nothing.

**The image is the first in prdb's list.** The order is documented and stable, so
this is a reproducible rule rather than a convention guessed at. It is worth
being exact about what that guarantee is: it fixes the *order*, not a *ranking*.
Nothing says the oldest image is the best or the most representative one — it is
chosen because two runs choose the same one, and that is the property a filing
decision needs.

**No extra request to prdb.** The `VideoDetailDto` that
[ADR 0024](0024-the-sidecar-is-written-by-filing-and-only-over-its-own.md)'s
batch lookup already fetches per run carries `Images`. The only new traffic is
the image itself, from the CDN, and only for a scene actually being filed.

**A scene prdb has no image for is not a failure.** The image array may be
empty, and `cdnPath` is nullable — the schema says "if available" and means it.
Nothing is written, nothing is said beyond the row's own account of the run, and
the item is the same clean one that artwork being switched off produces. A scene
with no artwork is a state this layout was measured against; treating it as a
problem would turn the ordinary case into a warning.

**A failed download never fails a filing.** Same rule as the sidecar: nothing
here can undo the move above and nothing here may try. The video is filed, the
row says what did not arrive next to it, and the library shows an item without a
backdrop — which section 5 measured to be a perfectly good item.

**A performer's image is not part of this.** A `<thumb>` inside an `<actor>` is
one line in the sidecar and makes the media server fetch images from the
internet, which is what somebody who left artwork off did not ask for — and it
was never measured. ADR 0024 rejected it once; it stays rejected, and it is a
different feature if it is ever wanted.

## Alternatives considered

- **Write `poster.jpg` as well, or instead.** Rejected on the measurement in
  [#36](https://github.com/prdb-net/prdb-ordeno/pull/36), which is the whole
  reason this decision differs from the issue that asked for it. It costs a
  second download to make the library grid blurrier than writing nothing does.
- **Choose the file name from the downloaded image's shape** — `poster.jpg` when
  it is portrait, `fanart.jpg` when it is not. Rejected, and this is the one
  worth stating: the aspect ratio is only knowable after the bytes arrive, so the
  name would depend on something the preview has not seen. The hard rule says
  every write path can be asked what it would do without doing it, and *that
  answer is exactly what the real run performs*. A plan saying "an image" and a
  run writing a name it picked afterwards is that rule quietly broken for a case
  prdb's images almost never produce.
- **Put a marker in the JPEG** — an EXIF or `COM` comment, the direct analogue of
  the sidecar's XML comment. Rejected. It means the file on disk is not what prdb
  served, it adds a metadata writer and a class of failure to a path whose whole
  job is to be interruptible, and it buys a replacement that nothing is asking
  for.
- **Keep a record of the images written, in the database.** Rejected for the
  reason ADR 0024 rejected a hidden marker file: two things to keep in step, one
  of which a user copying a directory takes and the other of which they do not,
  and "is this file mine" answered by something other than the file.
- **Replace whatever is at the name.** Rejected outright by the hard rule. An
  image a user made or chose is work somebody did, and losing it is not
  recoverable.
- **Download while working out the preview.** Rejected, exactly as ADR 0024
  rejected asking prdb there. A preview is safe to press and may be pressed
  repeatedly; spending bandwidth to produce a sentence the user may not act on is
  the pattern [ADR 0001](0001-identification-runs-in-prdb.md) exists to avoid.
  The preview says an image would be written; the run finds out whether it
  arrives.
- **Write prdb's image URL into the sidecar as `<thumb>` and download nothing.**
  Rejected in ADR 0024 and still rejected. It is cheap to write and it makes the
  media server fetch from the internet, which is what the setting exists to
  prevent.

## Consequences

- With artwork on, a filed scene gets one image, and the library shows it as the
  backdrop on the detail page and as the card in the grid — which #36 measured to
  be the best available outcome for images shaped the way prdb's are.
- With artwork off, nothing is requested and nothing is written, and the item is
  a clean one. Section 5 measured that absence costs nothing, which is what makes
  the default cheap rather than degraded.
- A second quality filed into a scene directory that already has its image
  downloads nothing. The rule that protects a user's file also removes the
  repeat.
- A scene filed before prdb changed its image keeps the old one until somebody
  deletes it. That is the same gap ADR 0024 named for the sidecar, and it closes
  with the same decision — the refresh policy, now
  [#38](https://github.com/prdb-net/prdb-ordeno/issues/38), which inherits this
  as well as the sidecar. If a refresh is ever allowed to replace an image, that
  is an amendment to this decision and needs its own reasoning rather than
  arriving as a side effect.
- Filing now spends bandwidth when the setting is on: one image per scene filed,
  once. A run that files nothing downloads nothing.
- An image a user put there survives everything the tool does, and there is a
  documented way to ask for a new one that needs no setting: delete the file.
