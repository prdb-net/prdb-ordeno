namespace Prdb.Ordeno.Infrastructure.Tests;

/// <summary>
/// A real directory on a real filesystem, removed when the test ends. AGENTS.md
/// asks the destructive paths to be tested against one of these rather than a
/// mocked file layer, because the failures worth catching — a half-finished
/// cross-device copy, a file still being written, a target that already exists —
/// are the ones a mock cannot have.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"prdb-ordeno-tests-{Guid.NewGuid():n}");

        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Combine(string name) => System.IO.Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover directory under the system temp path is not worth
            // failing a test that has already made its point.
        }
    }
}
