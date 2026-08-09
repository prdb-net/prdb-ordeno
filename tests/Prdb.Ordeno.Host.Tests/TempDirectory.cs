namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// A real directory for the database a test's application writes, removed when
/// the test ends.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), $"prdb-ordeno-host-tests-{Guid.NewGuid():n}");

        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test that has already made its point.
        }
    }
}
