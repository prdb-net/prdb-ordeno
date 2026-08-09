using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.Access;

/// <summary>
/// The one password and the sessions it hands out (ADR 0010). No accounts, no
/// roles, no recovery by email — whoever gets past this can move and delete
/// files that cannot be got back, which is the whole reason it exists on a LAN
/// tool at all.
/// </summary>
public sealed class AccessService(
    OrdenoDbContext context,
    IPasswordHasher<StoredConfiguration> hasher,
    TimeProvider time,
    ILogger<AccessService> logger)
{
    /// <summary>
    /// Length is the only rule. Composition rules push people towards
    /// "Password1!" and buy nothing here, where there is no username to go with
    /// it and sign-in is throttled anyway.
    /// </summary>
    public const int MinimumPasswordLength = 8;

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    /// <summary>Renewed once it has less than this left, not on every request.</summary>
    private static readonly TimeSpan RenewWhenLessThan = TimeSpan.FromDays(15);

    public async Task<bool> IsPasswordSetAsync(CancellationToken cancellationToken = default) =>
        await context.Configuration.AnyAsync(row => row.PasswordHash != null, cancellationToken);

    public async Task<SetInitialPasswordResult> SetInitialPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (password.Length < MinimumPasswordLength)
        {
            return new SetInitialPasswordResult(SetInitialPasswordStatus.TooShort, SessionToken: null);
        }

        var configuration = await SingleConfigurationAsync(cancellationToken);
        if (configuration.PasswordHash is not null)
        {
            return new SetInitialPasswordResult(SetInitialPasswordStatus.AlreadySet, SessionToken: null);
        }

        configuration.PasswordHash = hasher.HashPassword(configuration, password);
        configuration.PasswordSetAt = time.GetUtcNow();

        var token = IssueSession();
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("The password was set. The setup path is closed from here on.");

        return new SetInitialPasswordResult(SetInitialPasswordStatus.Set, token);
    }

    public async Task<SignInResult> SignInAsync(string password, CancellationToken cancellationToken = default)
    {
        var configuration = await SingleConfigurationAsync(cancellationToken);
        if (configuration.PasswordHash is null)
        {
            return new SignInResult(Succeeded: false, SessionToken: null);
        }

        var verification = hasher.VerifyHashedPassword(configuration, configuration.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("A sign-in attempt was refused.");
            return new SignInResult(Succeeded: false, SessionToken: null);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            configuration.PasswordHash = hasher.HashPassword(configuration, password);
        }

        var token = IssueSession();
        await context.SaveChangesAsync(cancellationToken);

        return new SignInResult(Succeeded: true, token);
    }

    /// <summary>
    /// Returns the session the token belongs to, or <c>null</c> if there is
    /// none, it has expired, or it has been revoked. Expired rows are removed as
    /// they are met, which keeps the table from growing without a job to sweep
    /// it.
    /// </summary>
    public async Task<Session?> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = SessionToken.Hash(token);
        var session = await context.Sessions.SingleOrDefaultAsync(row => row.TokenHash == hash, cancellationToken);

        if (session is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (session.ExpiresAt <= now)
        {
            context.Sessions.Remove(session);
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }

        // Writing on every request would put the single SQLite writer (ADR 0007)
        // in the path of every page load for no benefit.
        if (session.ExpiresAt - now < RenewWhenLessThan)
        {
            session.LastSeenAt = now;
            session.ExpiresAt = now + SessionLifetime;
            await context.SaveChangesAsync(cancellationToken);
        }

        return session;
    }

    public async Task SignOutAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = SessionToken.Hash(token);

        await context.Sessions.Where(row => row.TokenHash == hash).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Forgets the password and every session, leaving the installation in the
    /// state a fresh one is in: the setup path open and nothing else. This is
    /// the documented way back in for someone who has lost the password, and it
    /// is reachable only from the machine the data directory is mounted on.
    /// </summary>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await SingleConfigurationAsync(cancellationToken);

        configuration.PasswordHash = null;
        configuration.PasswordSetAt = null;

        await context.Sessions.ExecuteDeleteAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "The password and all sessions were cleared on request. The next visitor sets a new "
            + "password, so remove the reset setting before this container restarts again.");
    }

    /// <summary>
    /// Adds the session to the change tracker; the caller saves it together with
    /// whatever else it changed, so a password that is set and a session that is
    /// handed out for it land in one transaction.
    /// </summary>
    private string IssueSession()
    {
        var token = SessionToken.Create();
        var now = time.GetUtcNow();

        context.Sessions.Add(new Session
        {
            TokenHash = SessionToken.Hash(token),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + SessionLifetime,
        });

        return token;
    }

    private async Task<StoredConfiguration> SingleConfigurationAsync(CancellationToken cancellationToken) =>
        await context.Configuration.SingleAsync(cancellationToken);
}
