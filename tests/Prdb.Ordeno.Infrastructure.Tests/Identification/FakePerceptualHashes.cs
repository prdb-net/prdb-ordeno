using Prdb.Ordeno.Core.Identification;

namespace Prdb.Ordeno.Infrastructure.Tests.Identification;

/// <summary>
/// ffmpeg's answer, as a test decides it. The real one decodes twenty-five
/// frames of a real video, which is neither something to put in a repository nor
/// something to wait for in a test — and what the backlog has to get right is
/// what it does with the answer, not how the answer was arrived at.
/// </summary>
internal sealed class FakePerceptualHashes : IPerceptualHashes
{
    private Func<string, PerceptualHashReading> answer =
        _ => new PerceptualHashReading(PerceptualHashState.Computed, "0123456789abcdef");

    /// <summary>Every file it was asked about, in order.</summary>
    public List<string> Hashed { get; } = [];

    public void Answers(Func<string, PerceptualHashReading> with) => answer = with;

    public void Fails(PerceptualHashState state) =>
        Answers(_ => new PerceptualHashReading(state, Error: "ffmpeg said no."));

    public Task<PerceptualHashReading> ComputeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        Hashed.Add(Path.GetFileName(path));

        return Task.FromResult(answer(path));
    }
}
