using Microsoft.Extensions.Logging;

using Prdb.Hashing;
using Prdb.Ordeno.Core.Identification;

namespace Prdb.Ordeno.Infrastructure.Identification;

/// <summary>
/// The exact hash, from the <c>Prdb.Hashing</c> package. It is not computed here
/// and it is not corrected here — ADR 0004: the package reproduces Stash's
/// quirks on purpose, and a value that differs from everyone else's matches
/// nothing.
/// </summary>
public sealed class OsHashes(ILogger<OsHashes> logger) : IFileHashes
{
    public OsHashReading OsHashOf(string path)
    {
        // TryCompute rather than Compute: a file being written or moved is a
        // normal state in a download directory, and "I could not read it" is a
        // different answer from "it has no hash". Recording the first as the
        // second would cost the file its exact-hash rung permanently.
        if (!OsHash.TryCompute(path, out var hash))
        {
            logger.LogDebug("{Path} could not be read for its hash; it is left for the next run.", path);

            return OsHashReading.Unreadable;
        }

        if (hash is not null)
        {
            return OsHashReading.Of(hash);
        }

        // The package answers null for a file under 128 KiB and for one that is
        // not there. Only the first is a settled answer.
        return File.Exists(path) ? OsHashReading.TooSmall : OsHashReading.Unreadable;
    }
}
