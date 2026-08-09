using System.Net;
using System.Net.Http.Json;

using Prdb.Ordeno.Host.Access;

using Xunit;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// ADR 0010 over HTTP: the setup path is the only unauthenticated write there
/// is, it closes the moment it has been used, and everything else needs the
/// cookie it hands out.
/// </summary>
public sealed class AccessTests
{
    private const string Password = "a-password-nobody-guesses";

    [Fact]
    public async Task A_fresh_installation_asks_for_a_password_and_nothing_else()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = application.CreateClient();

        var state = await client.GetFromJsonAsync<AccessState>("/api/access/state");

        Assert.NotNull(state);
        Assert.False(state.PasswordSet);
        Assert.False(state.Authenticated);
    }

    [Fact]
    public async Task Setting_the_first_password_signs_the_visitor_in()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("ordeno_session=", StringComparison.Ordinal));

        var state = await client.GetFromJsonAsync<AccessState>("/api/access/state");
        Assert.True(state!.Authenticated);
    }

    [Fact]
    public async Task The_setup_path_closes_once_a_password_exists()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = application.CreateClient();

        await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));

        // A second visitor — no cookie, no password of their own.
        using var stranger = application.CreateClient();
        var response = await stranger.PostAsJsonAsync(
            "/api/access/password",
            new SetPasswordRequest("something-else-entirely"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_password_shorter_than_the_minimum_is_refused()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest("short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var state = await client.GetFromJsonAsync<AccessState>("/api/access/state");
        Assert.False(state!.PasswordSet);
    }

    [Fact]
    public async Task Without_a_session_a_protected_endpoint_is_refused()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = application.CreateClient();

        var response = await client.DeleteAsync("/api/access/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_right_password_opens_a_session_and_the_wrong_one_does_not()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);

        using (var setup = application.CreateClient())
        {
            await setup.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));
        }

        using var wrong = application.CreateClient();
        var refused = await wrong.PostAsJsonAsync("/api/access/session", new SignInRequest("not-the-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using var right = application.CreateClient();
        var accepted = await right.PostAsJsonAsync("/api/access/session", new SignInRequest(Password));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var signedOut = await right.DeleteAsync("/api/access/session");
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
    }

    [Fact]
    public async Task Signing_out_makes_the_session_useless()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = application.CreateClient();

        var setUp = await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));
        var token = SessionCookieValue(setUp);

        await client.DeleteAsync("/api/access/session");

        // The same token, presented again by hand: the row behind it is gone.
        using var revoked = application.CreateClient();
        revoked.DefaultRequestHeaders.Add("Cookie", $"ordeno_session={token}");

        var response = await revoked.DeleteAsync("/api/access/session");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_session_survives_a_restart()
    {
        using var directory = new TempDirectory();
        string token;

        await using (var first = new OrdenoApplication(directory.Root))
        using (var client = first.CreateClient())
        {
            var response = await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));
            token = SessionCookieValue(response);
        }

        await using var restarted = new OrdenoApplication(directory.Root);
        using var afterRestart = restarted.CreateClient();
        afterRestart.DefaultRequestHeaders.Add("Cookie", $"ordeno_session={token}");

        var state = await afterRestart.GetFromJsonAsync<AccessState>("/api/access/state");

        Assert.True(state!.Authenticated);
    }

    [Fact]
    public async Task Repeated_attempts_are_throttled()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = application.CreateClient();

        // Setting the password counts against the same limiter — it is the same
        // door — so four attempts remain of the five per minute.
        await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var refused = await client.PostAsJsonAsync("/api/access/session", new SignInRequest("wrong"));
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync("/api/access/session", new SignInRequest("wrong"));

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    [Fact]
    public async Task The_reset_setting_opens_the_setup_path_again()
    {
        using var directory = new TempDirectory();

        await using (var first = new OrdenoApplication(directory.Root))
        using (var client = first.CreateClient())
        {
            await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));
        }

        await using var reset = new OrdenoApplication(directory.Root, resetPassword: true);
        using var afterReset = reset.CreateClient();

        var state = await afterReset.GetFromJsonAsync<AccessState>("/api/access/state");
        Assert.False(state!.PasswordSet);

        var response = await afterReset.PostAsJsonAsync(
            "/api/access/password",
            new SetPasswordRequest("a-completely-new-password"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string SessionCookieValue(HttpResponseMessage response)
    {
        var cookie = response.Headers
            .GetValues("Set-Cookie")
            .Single(value => value.StartsWith("ordeno_session=", StringComparison.Ordinal));

        return cookie.Split(';')[0]["ordeno_session=".Length..];
    }
}
