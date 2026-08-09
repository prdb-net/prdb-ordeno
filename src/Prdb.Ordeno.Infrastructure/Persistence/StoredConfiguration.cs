namespace Prdb.Ordeno.Infrastructure.Persistence;

/// <summary>
/// The single row holding what onboarding collects (ADR 0009). Everything on it
/// is nullable because a fresh installation has answered none of it yet, and the
/// tool scans nothing until <see cref="OnboardingCompletedAt"/> is set.
/// </summary>
public sealed class StoredConfiguration
{
    /// <summary>
    /// This is a one-row table; a check constraint in the schema says so too.
    /// </summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>
    /// Kept as the user gave it. ADR 0009 is plain about this: the tool stores
    /// the key, and the documentation does not pretend that a file on the user's
    /// own NAS is a secret store. It is never logged and never returned to the
    /// browser once saved.
    /// </summary>
    public string? PrdbApiKey { get; set; }

    public string? TargetDirectory { get; set; }

    /// <summary>
    /// The media server layout. One value exists in the first release, Jellyfin
    /// (ADR 0008); the column carries the name rather than a number so that a
    /// second one reads as itself in the database.
    /// </summary>
    public string? Layout { get; set; }

    public DateTimeOffset? OnboardingCompletedAt { get; set; }
}
