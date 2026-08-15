# 0021. A cross-filesystem copy is verified by size and `osHash`

- **Status:** Accepted
- **Date:** 2026-08-15

## Context

[ADR 0002](0002-files-are-moved-not-copied.md) files by moving, and the hard
rule in `AGENTS.md` spells out what that means when the download directory and
the library are not on one filesystem: copy, verify, then delete — in that
order, with the delete conditional on the verification.

That makes "verify" the load-bearing word. It is the only thing standing between
a copy that went wrong and a `File.Delete` on the user's only copy, and it is
also the slow path already: this is the case where a 30 GB file crosses an SMB
mount, on hardware bought for storing films rather than for moving them.

Three verifications are available at three prices. The file is written by this
process, so the failures worth catching are the ones a write can actually have —
a share that filled up, a mount that went away mid-copy, a container stopped
halfway, a target path that turned out to be something else — rather than an
adversary substituting a file.

## Decision

**Size, then `osHash`, computed fresh on both sides after the copy has been
flushed and closed.**

`osHash` comes from `Prdb.Hashing`, where the tool already has it, and reads the
first and last 64 KiB together with the length — a few hundred kilobytes rather
than the whole file, and the two ends are exactly where a truncated or
never-finished copy differs.

**A file below `OsHash.MinimumFileSize` has no `osHash`** — that is the
package's contract, and `PackageContractTests` holds it. Those files are
verified by comparing every byte instead. 128 KiB is nothing to read, so the one
case where the cheap check is unavailable is also the one where the exhaustive
check is free.

Both hashes are computed for this purpose, and neither is the one the
identification path stored against the file. That value was read before the copy
and says nothing about whether the copy is sound; reusing it would be verifying
a write against a claim made before it happened.

The source is deleted only after the comparison succeeds. It is not deleted
when the copy fails, when the verification fails, or when the run is cancelled
between the two — see the shutdown behaviour in `AGENTS.md`. What is left behind
in those cases is a partial file at the target under a temporary name, which the
tool removes on its way out and which is not a name the media server reads.

## Alternatives considered

- **Read both files back in full and compare, or hash them.** The exhaustive
  answer, and the only one that catches a flipped bit in the middle. Rejected as
  the default: it doubles the read work on the path that is already the slow one,
  which on this audience's hardware is the difference between filing a library
  overnight and filing it over a weekend — to defend against silent corruption,
  which is what the filesystem's own checksums and the storage exist to handle,
  and which the tool could not fix if it found it.
- **Compare the size and nothing else.** Catches the interrupted copy and the
  full disk, which is most of what goes wrong. Rejected because it costs almost
  nothing to do better: a file can reach the right length and hold the wrong
  bytes at either end, and this check is the last thing that happens before a
  delete the user cannot undo.
- **Reuse the `osHash` identification already stored.** Free, and wrong: it is a
  reading of the source taken minutes or days earlier, so a copy verified
  against it is verified against nothing that happened during the copy.

## Consequences

- Verification reads a fixed few hundred kilobytes per file whatever its size, so
  the cost of filing stays the cost of the copy.
- A flipped bit in the middle of a copied file is not detected. That is stated
  here so it is a known limit rather than an implied promise, and it is the same
  limit every tool that does not re-read has.
- A file the tool cannot hash on either side is not verified, so its source is
  not deleted. It is reported instead: a library that gained a copy while the
  download directory kept the original is untidy, and the other outcome is not.
- Within one filesystem none of this happens. A rename is atomic, there is no
  second copy to compare, and adding a verification there would only be a
  ceremony over an operation that cannot half-happen.
