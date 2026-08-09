namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// One watched directory. There can be several — VISION.md says downloads
/// accumulate wherever the user's download client puts them, which is not always
/// one place.
/// </summary>
public sealed class SourceDirectory
{
    public int Id { get; set; }

    public required string Path { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}
