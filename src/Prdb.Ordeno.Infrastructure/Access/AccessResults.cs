namespace Prdb.Ordeno.Infrastructure.Access;

public enum SetInitialPasswordStatus
{
    /// <summary>The password was set, and <c>SessionToken</c> signs the user in.</summary>
    Set,

    /// <summary>
    /// A password already exists. Setting the first one is the only
    /// unauthenticated write path there is (ADR 0010), so it closes the moment
    /// it has been used.
    /// </summary>
    AlreadySet,

    /// <summary>Shorter than <see cref="AccessService.MinimumPasswordLength"/>.</summary>
    TooShort,
}

public sealed record SetInitialPasswordResult(SetInitialPasswordStatus Status, string? SessionToken);

public sealed record SignInResult(bool Succeeded, string? SessionToken);
