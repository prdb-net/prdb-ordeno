using Microsoft.Extensions.Logging;

using Prdb.Hashing;
using Prdb.Ordeno.Core.Identification;

namespace Prdb.Ordeno.Infrastructure.Identification;

/// <summary>
/// The perceptual hash, from the <c>Prdb.Hashing</c> package, which shells out
/// to the ffmpeg and ffprobe the image ships. ADR 0004 applies here too: the
/// method is transcribed from a specification and is not this repository's to
/// improve.
/// </summary>
public sealed class PerceptualHashes(ILogger<PerceptualHashes> logger) : IPerceptualHashes
{
    private readonly VideoPerceptualHasher hasher = new();

    public async Task<PerceptualHashReading> ComputeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await hasher.ComputeAsync(path, cancellationToken);

            if (result.UsedAccurateSeek)
            {
                logger.LogDebug(
                    "{Path} needed an accurate seek to be hashed, which is slow but correct.",
                    path);
            }

            // Lowercase on the way in, whatever produced it: everything local
            // compares bytes, and a hash stored in the other casing never
            // matches — silently.
            return new PerceptualHashReading(
                Map(result.Outcome),
                FileHashes.Normalize(result.Hash),
                result.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The package returns its failures rather than throwing, so this is
            // the case it did not foresee. It is recorded against the file all
            // the same: a backlog that stops on one file stops for every file
            // behind it.
            logger.LogWarning(exception, "Hashing {Path} threw where the package answers instead.", path);

            return new PerceptualHashReading(PerceptualHashState.Failed, Error: exception.Message);
        }
    }

    private static PerceptualHashState Map(PerceptualHashOutcome outcome) => outcome switch
    {
        PerceptualHashOutcome.Computed => PerceptualHashState.Computed,
        PerceptualHashOutcome.SourceMissing => PerceptualHashState.SourceMissing,
        PerceptualHashOutcome.ProbeFailed => PerceptualHashState.ProbeFailed,
        PerceptualHashOutcome.FrameCaptureFailed => PerceptualHashState.FrameCaptureFailed,
        PerceptualHashOutcome.FrameDecodeFailed => PerceptualHashState.FrameDecodeFailed,
        PerceptualHashOutcome.TimedOut => PerceptualHashState.TimedOut,

        // A newer package with an outcome this build has no name for. It is a
        // failure either way, and one that is not worth trying again.
        _ => PerceptualHashState.Failed,
    };
}
