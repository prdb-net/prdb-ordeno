namespace Prdb.Ordeno.Core.Identification;

/// <summary>
/// Which rung of the recognition ladder answered for a file. prdb walks the
/// whole ladder server-side and names the rung it stopped on — ADR 0001 — so
/// this is a reading of what came back, never a decision made here.
/// </summary>
/// <remarks>
/// It is the tool's own type rather than the SDK's because Core sees no SDK, and
/// because a rung the tool does not recognise has to be survivable: prdb may add
/// one, and a new number arriving in an old container must not turn a good
/// answer into an exception.
/// </remarks>
public enum MatchRung
{
    /// <summary>
    /// The exact file hash. Somebody has already identified this very file, and
    /// it stays recognised whatever it has been renamed to.
    /// </summary>
    OsHash,

    /// <summary>
    /// The perceptual hash. prdb still compares it for equality rather than by
    /// distance, so today this rung is only a more expensive
    /// <see cref="OsHash"/>. The backlog that computes them exists because
    /// computing them is the slow part and has to be done before distance
    /// matching could ever be useful — not because it helps recognition now.
    /// </summary>
    PerceptualHash,

    /// <summary>A file name prdb has stored against a video.</summary>
    FileName,

    /// <summary>The file name, without its extension, read as a scene release title.</summary>
    ReleaseName,

    /// <summary>
    /// Only the site could be read out of the name. This is a result and not a
    /// failure: a file known to be from one site is further along than one the
    /// tool knows nothing about.
    /// </summary>
    Site,
}

/// <summary>
/// How far an answer can be trusted, as prdb graded it.
/// </summary>
/// <remarks>
/// <see cref="Ambiguous"/> is not a weaker <see cref="Probable"/>. It means
/// several videos fitted equally well and prdb declined to choose — the
/// candidates are the answer, and choosing between them is a person's job.
/// </remarks>
public enum MatchConfidence
{
    /// <summary>Nothing matched. Not an error; a file the ladder ran out on.</summary>
    None,

    Partial,

    Probable,

    Strong,

    Exact,

    /// <summary>Several videos fitted equally well, so no video was named.</summary>
    Ambiguous,
}
