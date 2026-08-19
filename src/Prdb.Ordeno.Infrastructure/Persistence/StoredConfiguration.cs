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

    /// <summary>
    /// Where the media server answers, or <c>null</c> because it was left blank —
    /// which is the default and not a degraded state (ADR 0018). Stored
    /// normalised, so that what onboarding reads back is what the tool will send
    /// to rather than what somebody typed.
    /// </summary>
    public string? MediaServerUrl { get; set; }

    /// <summary>
    /// The key that gets into it, under the same rule as
    /// <see cref="PrdbApiKey"/>: never logged, never returned to the browser once
    /// saved. It is only ever set together with <see cref="MediaServerUrl"/> — a
    /// key with no address reaches nothing, and an address with no key is
    /// refused by everything worth asking.
    /// </summary>
    public string? MediaServerApiKey { get; set; }

    /// <summary>
    /// Whether filing downloads one image per scene — ADR 0027. Not nullable and
    /// false by default, unlike everything above it: this is not something
    /// onboarding is waiting for an answer to, it is a switch that is off until
    /// somebody turns it on.
    /// </summary>
    public bool DownloadArtwork { get; set; }

    /// <summary>
    /// Whether the tool files on its own, every <c>FilingSchedule.Interval</c> —
    /// ADR 0031. False by default, and false for an installation that is upgraded
    /// into the release that adds it: a tool that starts moving files because it
    /// was upgraded is the surprise the opt-in rule exists to prevent.
    /// </summary>
    public bool UnattendedFiling { get; set; }

    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    /// <summary>
    /// The one password, hashed (ADR 0010). Null means a fresh installation
    /// where the setup path is still open — it is what "no password has been set
    /// yet" is read from, so nothing else may write it.
    /// </summary>
    public string? PasswordHash { get; set; }

    public DateTimeOffset? PasswordSetAt { get; set; }
}
