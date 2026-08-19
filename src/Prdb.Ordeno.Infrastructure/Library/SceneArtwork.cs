using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Core.Library;

namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>How writing one image ended.</summary>
public enum ArtworkWriteState
{
    /// <summary>There was none, and now there is.</summary>
    Written,

    /// <summary>
    /// There is a file at that name already, or one that could not be looked at.
    /// Nothing was downloaded and nothing was lost.
    /// </summary>
    Kept,

    /// <summary>
    /// It could not be fetched or could not be written. The video is filed and
    /// has no image, which section 5 of the layout document measured to be a
    /// perfectly good item.
    /// </summary>
    Failed,
}

/// <param name="Problem">What to tell the user. <c>null</c> when there is nothing to say.</param>
public sealed record ArtworkOutcome(ArtworkWriteState State, string? Problem = null)
{
    public bool Wrote => State is ArtworkWriteState.Written;
}

/// <summary>
/// The one image in a scene directory: whether there is one, and how one arrives
/// without ever being half of anything.
/// </summary>
/// <remarks>
/// <para>
/// <see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/adr/0027-artwork-is-one-image-written-only-where-there-is-none.md">ADR 0027</see>
/// is most of this class. It writes where there is nothing and nowhere else, so
/// it has no idea which images are its own and does not need one: a tool that
/// never replaces need not recognise its own work, and deleting the file is how
/// a user asks for a fresh one.
/// </para>
/// <para>
/// The two things it insists on before keeping any bytes are that there are not
/// too many of them and that they are a JPEG. The URL is one the tool did not
/// compose and the response is not one it controls; a scene directory is not a
/// place to put whatever answered.
/// </para>
/// <para>
/// Nothing here throws. A failed download must not fail a filing — the video has
/// already moved by the time this runs — so every way this can go wrong,
/// including the container being asked to stop mid-download, comes back as
/// <see cref="ArtworkWriteState.Failed"/> and a sentence.
/// </para>
/// </remarks>
public sealed class SceneArtwork(IHttpClientFactory clients, ILogger<SceneArtwork> logger) : ISceneArtwork
{
    /// <summary>The named client images are fetched over — prdb's CDN, not its API.</summary>
    public const string HttpClientName = "artwork";

    /// <summary>
    /// As much of an image as the tool will keep. prdb's are a few hundred
    /// kilobytes; this is far above that and far below anything that would
    /// matter on a NAS, and it exists so that a response that is not what it
    /// claims cannot fill somebody's library directory.
    /// </summary>
    public const int MaximumBytes = 16 * 1024 * 1024;

    /// <summary>
    /// How far from the end the end-of-image marker may sit. Zero for most
    /// encoders; a handful of encoders and every proxy that appends a newline
    /// need the slack, and a truncated download has no marker at all.
    /// </summary>
    private const int TrailerBytes = 32;

    public ArtworkState StateOf(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        try
        {
            // A directory at that name is in the way as surely as a file is.
            return File.Exists(absolutePath) || Directory.Exists(absolutePath)
                ? ArtworkState.Present
                : ArtworkState.Missing;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not look at the image {Path}.", absolutePath);

            return ArtworkState.Unknown;
        }
    }

    /// <summary>
    /// Fetches <paramref name="url"/> and puts it at <paramref name="absolutePath"/>,
    /// if and only if there is nothing at that path.
    /// </summary>
    /// <param name="url">
    /// An absolute URL from prdb's answer. It is checked for being one: the tool
    /// did not compose it, and a request built from an unvalidated string is a
    /// request to wherever the string said.
    /// </param>
    public async Task<ArtworkOutcome> DownloadAsync(
        string url,
        string absolutePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        // Asked again, now. The plan the user read was worked out earlier, and
        // this is the last moment before a directory is written into.
        if (StateOf(absolutePath) is not ArtworkState.Missing)
        {
            return new ArtworkOutcome(ArtworkWriteState.Kept, Already(absolutePath));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("prdb named an image at something that is not an http address.");

            return new ArtworkOutcome(
                ArtworkWriteState.Failed,
                "prdb named the image for this scene at something that is not a web address, so "
                + "nothing was downloaded. The video is filed.");
        }

        byte[] image;

        try
        {
            image = await FetchAsync(address, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
            or OperationCanceledException or InvalidDataException)
        {
            // OperationCanceledException among them deliberately. The video has
            // already moved and been written down; a stop that lands in the
            // middle of a download is one filed video without an image, not a
            // filing that failed.
            logger.LogWarning(exception, "Could not fetch the image for {Path}.", absolutePath);

            return new ArtworkOutcome(
                ArtworkWriteState.Failed,
                "The video is filed, and the image that was to go next to it could not be "
                + $"downloaded: {exception.Message} Nothing was written, and the next filing into "
                + "that scene fetches it again.");
        }

        return Put(image, absolutePath);
    }

    /// <summary>
    /// The bytes, or an exception saying why there are none. A response claiming
    /// no length and then sending forever is stopped at the cap rather than
    /// followed to the end of the disk.
    /// </summary>
    private async Task<byte[]> FetchAsync(Uri address, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(HttpClientName);

        using var response = await client.GetAsync(
            address,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        // Said up front where the server says it, so an image far too large is
        // refused before it is fetched rather than after.
        if (response.Content.Headers.ContentLength > MaximumBytes)
        {
            throw TooLarge();
        }

        await using var reading = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var collected = new MemoryStream();

        // Read to the cap and not one block further. Copying the whole response
        // first and measuring afterwards would let anything that answered decide
        // how much memory this container uses.
        var block = new byte[64 * 1024];
        int read;

        while ((read = await reading.ReadAsync(block, cancellationToken)) > 0)
        {
            if (collected.Length + read > MaximumBytes)
            {
                throw TooLarge();
            }

            collected.Write(block, 0, read);
        }

        var image = collected.ToArray();

        if (!IsJpeg(image))
        {
            throw new InvalidDataException(
                "what answered was not a JPEG, whole or otherwise, so it was not written next to "
                + "the video.");
        }

        return image;
    }

    private static InvalidDataException TooLarge() => new(
        $"the image is larger than the {MaximumBytes / (1024 * 1024)} MB this tool will write into "
        + "a scene directory.");

    /// <summary>
    /// A JPEG, and a complete one. The leading marker says what it is; the
    /// end-of-image marker near the end says the download finished, which is the
    /// failure a size cap and a status code both miss.
    /// </summary>
    private static bool IsJpeg(ReadOnlySpan<byte> image)
    {
        if (image.Length < 4 || image[0] != 0xFF || image[1] != 0xD8 || image[2] != 0xFF)
        {
            return false;
        }

        var trailer = image[^Math.Min(TrailerBytes, image.Length)..];

        for (var index = 0; index < trailer.Length - 1; index++)
        {
            if (trailer[index] == 0xFF && trailer[index + 1] == 0xD9)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The same shape as the sidecar's write, one step stronger: the rename does
    /// not overwrite. A half-written image is not merely a bad picture — it is a
    /// file at the name that stops the next run writing the good one.
    /// </summary>
    private ArtworkOutcome Put(byte[] image, string absolutePath)
    {
        // Dotted, so that neither Jellyfin's scanner nor this tool's own walk
        // reads a picture that is still arriving. In the same directory, so that
        // putting it in place is a rename on one filesystem.
        var staged = Path.Combine(
            Path.GetDirectoryName(absolutePath)!,
            $".{Path.GetFileName(absolutePath)}.{Guid.NewGuid():n}.tmp");

        try
        {
            Stage(staged, image);

            // overwrite: false, which is the whole decision in one argument. If
            // something appeared at that name while this was downloading, it
            // wins.
            File.Move(staged, absolutePath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(staged);

            if (File.Exists(absolutePath))
            {
                return new ArtworkOutcome(ArtworkWriteState.Kept, Already(absolutePath));
            }

            logger.LogWarning(exception, "Could not write the image {Path}.", absolutePath);

            return new ArtworkOutcome(
                ArtworkWriteState.Failed,
                $"The video is filed, and '{Path.GetFileName(absolutePath)}' could not be written "
                + $"next to it: {exception.Message}");
        }

        logger.LogInformation("Wrote {Path}.", absolutePath);

        return new ArtworkOutcome(ArtworkWriteState.Written);
    }

    private static void Stage(string staged, byte[] image)
    {
        using var writing = new FileStream(
            staged,
            new FileStreamOptions
            {
                // CreateNew: the name carries a fresh guid, so anything already
                // there is a bug rather than a collision to work around.
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            });

        writing.Write(image);

        // To the disk rather than to the operating system's cache, for the
        // reason the sidecar does it: a NAS losing power between the rename and
        // the write reaching it would leave the directory pointing at a file
        // with nothing in it — and nothing would ever replace it.
        writing.Flush(flushToDisk: true);
    }

    private void Discard(string staged)
    {
        try
        {
            File.Delete(staged);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A dotted file in a scene directory that nothing reads. Saying so is
            // all there is to do about it.
            logger.LogWarning(exception, "Could not remove the unfinished image {Path}.", staged);
        }
    }

    private static string Already(string absolutePath) =>
        $"There is already a '{Path.GetFileName(absolutePath)}' in that directory, so nothing was "
        + "downloaded. It is left exactly as it is — deleting it is how you ask for a fresh one.";
}
