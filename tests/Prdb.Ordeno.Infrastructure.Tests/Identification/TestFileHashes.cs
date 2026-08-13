using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Infrastructure.Identification;

namespace Prdb.Ordeno.Infrastructure.Tests.Identification;

/// <summary>
/// The real hashing, with named files declared unreadable.
/// </summary>
/// <remarks>
/// A file locked by something else is the case worth testing and the hardest one
/// to arrange from a test: the behaviour depends on which platform is running it
/// and as which user. So the file is real and its hash is real, and only the
/// moment where the filesystem says no is put there on purpose.
/// </remarks>
internal sealed class TestFileHashes : IFileHashes
{
    private readonly OsHashes real = new(NullLogger<OsHashes>.Instance);

    public HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

    public OsHashReading OsHashOf(string path) =>
        Unreadable.Contains(Path.GetFileName(path)) ? OsHashReading.Unreadable : real.OsHashOf(path);
}
