using System.Xml.Linq;

using Xunit;

namespace Prdb.Ordeno.Core.Tests;

/// <summary>
/// ADR 0012 fixes the dependency direction, and the point of that decision is
/// that it is enforced rather than agreed. These tests read the project files
/// instead of the compiled assemblies: a reference that has been declared but is
/// not used yet compiles away, and that is precisely the one that would slip
/// through unnoticed.
/// </summary>
public sealed class ArchitectureTests
{
    [Fact]
    public void Core_declares_no_dependencies()
    {
        var core = Project("src/Prdb.Ordeno.Core/Prdb.Ordeno.Core.csproj");

        Assert.Empty(References(core, "PackageReference"));
        Assert.Empty(References(core, "ProjectReference"));
    }

    /// <summary>
    /// ADR 0015: the rule is about <c>src/</c>. A test project may drive the
    /// composition root — that is the only way to check the wiring itself — but
    /// no library may depend on it, or it stops being the place where everything
    /// comes together.
    /// </summary>
    [Fact]
    public void Nothing_in_src_references_the_host()
    {
        foreach (var project in SourceProjectsExcept("Prdb.Ordeno.Host.csproj"))
        {
            var references = References(XDocument.Load(project.FullName), "ProjectReference");

            Assert.DoesNotContain(references, reference =>
                reference.EndsWith("Prdb.Ordeno.Host.csproj", StringComparison.Ordinal));
        }
    }

    private static IEnumerable<string> References(XDocument project, string kind) =>
        project.Descendants(kind)
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Replace('\\', '/'));

    private static XDocument Project(string relativePath) =>
        XDocument.Load(Path.Combine(RepositoryRoot().FullName, relativePath));

    private static IEnumerable<FileInfo> SourceProjectsExcept(string fileName) =>
        new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, "src"))
            .EnumerateFiles("*.csproj", SearchOption.AllDirectories)
            .Where(project => project.Name != fileName);

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Prdb.Ordeno.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            $"No repository root above {AppContext.BaseDirectory}.");
    }
}
