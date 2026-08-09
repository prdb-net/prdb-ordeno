namespace Prdb.Ordeno.Infrastructure.Access;

/// <summary>
/// One signed-in browser. Sessions are rows rather than encrypted cookies
/// (ADR 0010), which is what lets a restart keep the user signed in and lets a
/// session be revoked from the outside.
/// </summary>
public sealed class Session
{
    public int Id { get; set; }

    /// <summary>
    /// The SHA-256 of the token in the cookie, never the token itself. Someone
    /// who copies this database off the NAS gets a list of hashes, not a way in.
    /// </summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
