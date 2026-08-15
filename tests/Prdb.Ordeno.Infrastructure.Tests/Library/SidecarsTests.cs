using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Infrastructure.Library;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// The sidecar on a real filesystem: whose it is, and what replacing it leaves
/// behind.
/// </summary>
/// <remarks>
/// The interesting failures here are the ones a mocked file layer cannot have,
/// which is why <c>AGENTS.md</c> asks for a temporary directory: a file that is
/// already there, a document written by somebody else, a directory that has gone
/// away between deciding and writing.
/// </remarks>
public sealed class SidecarsTests : IDisposable
{
    private const string ByHand = "<movie><title>The Name I Gave It</title></movie>";

    private static readonly SceneMetadata Metadata = new(
        Guid.Parse("0f3a1c2b-4d5e-6f70-8192-a3b4c5d6e7f8"),
        "Scene Title",
        new DateOnly(2025, 11, 7),
        "Example Studio",
        ["Someone Real"]);

    private readonly TempDirectory directory = new();
    private readonly Sidecars sidecars = new(NullLogger<Sidecars>.Instance);

    private string Path => System.IO.Path.Combine(directory.Root, ScenePath.SidecarFileName);

    public void Dispose() => directory.Dispose();

    [Fact]
    public void A_directory_with_no_sidecar_in_it_says_so() =>
        Assert.Equal(SidecarState.Missing, sidecars.StateOf(Path));

    [Fact]
    public void A_sidecar_is_written_where_there_is_none()
    {
        var outcome = sidecars.Write(Path, MovieNfo.For(Metadata));

        Assert.Equal(SidecarWriteState.Written, outcome.State);
        Assert.Null(outcome.Problem);
        Assert.True(File.Exists(Path));
    }

    /// <summary>
    /// The property the whole rewrite rests on, years later: what the tool wrote
    /// is something it can recognise as its own.
    /// </summary>
    [Fact]
    public void What_the_tool_writes_it_knows_again()
    {
        sidecars.Write(Path, MovieNfo.For(Metadata));

        Assert.Equal(SidecarState.Ours, sidecars.StateOf(Path));
    }

    [Fact]
    public void Its_own_sidecar_is_replaced()
    {
        sidecars.Write(Path, MovieNfo.For(Metadata));

        var outcome = sidecars.Write(Path, MovieNfo.For(Metadata with { Title = "A Corrected Title" }));

        Assert.Equal(SidecarWriteState.Replaced, outcome.State);
        Assert.Contains("A Corrected Title", File.ReadAllText(Path), StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>movie.nfo</c> somebody wrote by hand is not the tool's to overwrite,
    /// and the check is made at the moment of writing rather than only in the
    /// plan: the two are different moments, and the file may have appeared in
    /// between.
    /// </summary>
    [Fact]
    public void A_sidecar_somebody_else_wrote_is_left_exactly_as_it_is()
    {
        File.WriteAllText(Path, ByHand);

        var outcome = sidecars.Write(Path, MovieNfo.For(Metadata));

        Assert.Equal(SidecarWriteState.Kept, outcome.State);
        Assert.NotNull(outcome.Problem);
        Assert.Equal(ByHand, File.ReadAllText(Path));
        Assert.Equal(SidecarState.Foreign, sidecars.StateOf(Path));
    }

    /// <summary>
    /// The same answer for a directory sitting at the name. It is in the way as
    /// surely as a file is, and it is certainly not something the tool put
    /// there.
    /// </summary>
    [Fact]
    public void A_directory_at_that_name_is_not_written_over()
    {
        Directory.CreateDirectory(Path);

        Assert.Equal(SidecarState.Foreign, sidecars.StateOf(Path));
        Assert.Equal(SidecarWriteState.Kept, sidecars.Write(Path, MovieNfo.For(Metadata)).State);
        Assert.True(Directory.Exists(Path));
    }

    /// <summary>
    /// A write and a rename, so that the old document survives until the new one
    /// is complete — and so that a run interrupted between the two leaves one of
    /// them rather than half of either. What can be checked afterwards is the
    /// other half of that promise: nothing half-written is left lying about.
    /// </summary>
    [Fact]
    public void Replacing_a_sidecar_leaves_nothing_else_in_the_directory()
    {
        sidecars.Write(Path, MovieNfo.For(Metadata));
        sidecars.Write(Path, MovieNfo.For(Metadata with { Title = "Once More" }));
        sidecars.Write(Path, MovieNfo.For(Metadata with { Title = "And Again" }));

        Assert.Equal([ScenePath.SidecarFileName], Directory
            .GetFileSystemEntries(directory.Root)
            .Select(System.IO.Path.GetFileName));
    }

    /// <summary>
    /// The share went away between the video moving and its sidecar being
    /// written. It is one file with no metadata next to it, and nothing was left
    /// behind while finding out.
    /// </summary>
    [Fact]
    public void A_write_that_cannot_happen_says_so_and_leaves_nothing_behind()
    {
        var gone = System.IO.Path.Combine(directory.Root, "went-away", ScenePath.SidecarFileName);

        var outcome = sidecars.Write(gone, MovieNfo.For(Metadata));

        Assert.Equal(SidecarWriteState.Failed, outcome.State);
        Assert.NotNull(outcome.Problem);
        Assert.Empty(Directory.GetFileSystemEntries(directory.Root));
    }

    /// <summary>
    /// UTF-8, and no byte order mark in front of a declaration that says so.
    /// </summary>
    [Fact]
    public void A_sidecar_is_written_as_the_utf8_it_claims_to_be()
    {
        sidecars.Write(Path, MovieNfo.For(Metadata with { Title = "Ünicode — 名前 🎬" }));

        var bytes = File.ReadAllBytes(Path);

        Assert.NotEqual<byte[]>([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        Assert.Contains("Ünicode — 名前 🎬", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    /// <summary>
    /// A sidecar written without the marker is one the tool would refuse to
    /// touch again — a bug that shows up years later, so it is refused now.
    /// </summary>
    [Fact]
    public void A_document_that_is_not_one_of_ours_is_refused() =>
        Assert.Throws<ArgumentException>(() => sidecars.Write(Path, ByHand));
}
