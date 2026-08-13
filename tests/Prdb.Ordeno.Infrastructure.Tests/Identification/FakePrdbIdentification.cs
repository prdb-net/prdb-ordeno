using Prdb.Ordeno.Core.Identification;

namespace Prdb.Ordeno.Infrastructure.Tests.Identification;

/// <summary>
/// prdb's answer to a batch, as a test decides it. It replaces the endpoint and
/// nothing else: what the tool sends, what it does with what comes back, and
/// what it does when the answer is a refusal all run for real.
/// </summary>
/// <remarks>
/// The request is kept, because half of what this milestone promises is about
/// the question rather than the answer — that a file is asked about once, that
/// the hash goes with it, that a batch is a batch.
/// </remarks>
internal sealed class FakePrdbIdentification : IVideoIdentification
{
    private Func<IReadOnlyList<FileToIdentify>, IdentificationAnswer> answer =
        files => IdentificationAnswer.From([.. files.Select(Unrecognised)]);

    /// <summary>Every batch, in the order it was sent.</summary>
    public List<IReadOnlyList<FileToIdentify>> Batches { get; } = [];

    public List<string> ApiKeys { get; } = [];

    public IEnumerable<FileToIdentify> Asked => Batches.SelectMany(batch => batch);

    public void Answers(Func<IReadOnlyList<FileToIdentify>, IdentificationAnswer> with) => answer = with;

    /// <summary>Every file in the batch comes back as the one video below.</summary>
    public void Recognises(Guid videoId, string title, string site) => Answers(files =>
        IdentificationAnswer.From(
        [
            .. files.Select(file => new RecognisedFile(
                file.Ref,
                MatchConfidence.Exact,
                MatchRung.OsHash,
                videoId,
                title,
                new DateOnly(2024, 5, 1),
                Guid.NewGuid(),
                site,
                [])),
        ]));

    public Task<IdentificationAnswer> IdentifyAsync(
        string apiKey,
        IReadOnlyList<FileToIdentify> files,
        CancellationToken cancellationToken = default)
    {
        ApiKeys.Add(apiKey);
        Batches.Add(files);

        return Task.FromResult(answer(files));
    }

    private static RecognisedFile Unrecognised(FileToIdentify file) =>
        new(file.Ref, MatchConfidence.None, null, null, null, null, null, null, []);
}
