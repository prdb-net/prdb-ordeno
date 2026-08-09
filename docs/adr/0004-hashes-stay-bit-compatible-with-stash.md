# 0004. Hashes come from Prdb.Hashing and stay bit-compatible with Stash

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

`POST /videos/identify` is fed `osHash` and `pHash` values computed here. Those
values are only worth something if they match what everyone else computes for
the same file. prdb stores `pHash` as sixteen hex digits and validates nothing
beyond the shape, and two 64-bit values produced by different perceptual hashing
methods sit roughly 32 bits apart whether or not they describe the same video —
so a mismatched method does not produce a slightly worse result, it produces a
value that matches nothing.

The method in use is Stash's: an OpenSubtitles hash, and a 64-bit DCT hash over
a 5×5 montage of 25 frames. Stash is the established open-source program in this
space, so a value it would not produce is the foreign body in the corpus rather
than the other way round. prdb has since written the method down normatively
(`docs/video-hashing.md` in `prdb-sdk`, with test vectors as data) and published
`Prdb.Hashing`, which implements it.

The implementation reaches bit-compatibility by reproducing the original's
mistakes on purpose: an unusual resampler, a threshold function that is not the
median it is named after, cosine tables copied rather than recomputed because a
one-ulp difference flips a bit.

## Decision

Hashes come from the `Prdb.Hashing` package. Neither hash is reimplemented here,
and the package's oddities are not "corrected" — upstream or in a fork.

## Alternatives considered

- **Implement the hashes here.** Rejected: it duplicates a specification that
  has to be followed exactly, and the duplicate would drift silently.
- **Use a general-purpose perceptual hashing library.** Rejected: any library
  making sensible choices produces values incompatible with the corpus, which is
  the one thing that must not happen.

## Consequences

- A refactor that tidies the resampler, fixes the threshold function or
  recomputes the cosine tables produces hashes that match nothing. **No test of
  ours would catch it**, because the output stays internally consistent and only
  stops finding anything. This is why the prohibition is written down rather
  than left to good taste.
- `osHash` is `null` for a file under 128 KiB. That is a defined state, not an
  error to paper over.
- The perceptual hash decodes 25 frames per file, far too slow for the import
  path — it belongs in a background queue, and a file waiting for its hash must
  not hold up the filing of anything else.
- Failures come back as outcomes rather than exceptions, because a truncated
  download is routine on a real library.
- `ffmpeg` and `ffprobe` must be in the image.
- Values produced here are also usable by Stash, and vice versa.
- Until prdb compares perceptual hashes by distance rather than equality
  (prdb#28, deferred), that rung exists but carries no weight: compared exactly,
  a perceptual hash is only a worse `osHash`.
