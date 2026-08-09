namespace Prdb.Ordeno.Core.Configuration;

public enum ApiKeyStatus
{
    /// <summary>prdb answered as the key's owner.</summary>
    Valid,

    /// <summary>prdb answered, and said no.</summary>
    Refused,

    /// <summary>prdb did not answer, so nothing is known about the key.</summary>
    Unreachable,
}

/// <summary>
/// What prdb said about a key. The distinction that matters is between a key
/// that is wrong and a prdb that is down: only the first is the user's to fix,
/// and storing a key on the strength of the second would mean claiming it works.
/// </summary>
public sealed record ApiKeyCheck(ApiKeyStatus Status, string? Message)
{
    public bool Accepted => Status is ApiKeyStatus.Valid;
}

/// <summary>
/// Asks prdb whether a key works, before the tool stores it.
/// </summary>
public interface IPrdbApiKeyCheck
{
    Task<ApiKeyCheck> CheckAsync(string apiKey, CancellationToken cancellationToken = default);
}
