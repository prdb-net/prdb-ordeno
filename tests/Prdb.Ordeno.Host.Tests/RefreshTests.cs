using System.Net;
using System.Net.Http.Json;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Host.Configuration;
using Prdb.Ordeno.Host.Library;

using Xunit;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// ADR 0032 over HTTP, against the application as <c>Program.cs</c> composes it:
/// who may ask for a check, and that nothing checks anything until somebody does.
/// </summary>
/// <remarks>
/// What a run does is settled in <c>Prdb.Ordeno.Infrastructure.Tests</c>, where
/// a library can be written by hand and prdb can be made to change its mind.
/// What these tests are about is the wiring — including the one thing this area
/// deliberately does not have, which is a second endpoint to preview with.
/// </remarks>
public sealed class RefreshTests
{
    private const string Password = "a-password-nobody-guesses";
    private const string ApiKey = "the-only-key-prdb-knows";

    [Theory]
    [InlineData("GET", "/api/refresh")]
    [InlineData("POST", "/api/refresh")]
    public async Task Checking_the_library_is_behind_the_password(string method, string path)
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var stranger = application.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        using var response = await stranger.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A container that has just started has checked nothing, and says where the
    /// library stands in a shape the screen can render rather than leaving the
    /// section out.
    /// </summary>
    [Fact]
    public async Task A_fresh_installation_has_checked_nothing_and_has_nothing_to_check()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var state = await client.GetFromJsonAsync<RefreshState>("/api/refresh");

        Assert.NotNull(state);
        Assert.False(state.Running);
        Assert.False(state.Unattended);
        Assert.False(state.AskedByTimer);
        Assert.Equal(0, state.Scenes);
        Assert.Equal(0, state.NeverChecked);
        Assert.Null(state.WhatItDid);
        Assert.Empty(state.Changed);

        // The two numbers the screen builds its sentences from, so that the
        // browser holds no second copy of the schedule.
        Assert.Equal(24, state.IntervalHours);
        Assert.Equal(500, state.Slice);
    }

    /// <summary>
    /// The switch is off on a fresh installation and on one upgraded into the
    /// release that added it — the opt-in rule, applied to the second unattended
    /// write path in the tool.
    /// </summary>
    [Fact]
    public async Task The_unattended_check_is_off_until_it_is_turned_on()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var fresh = await client.GetFromJsonAsync<ConfigurationState>("/api/configuration");
        Assert.NotNull(fresh);
        Assert.False(fresh.RefreshesMetadata);

        using var response = await client.PutAsJsonAsync(
            "/api/configuration/unattended-refresh",
            new SetUnattendedRefreshRequest(true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var changed = await response.Content.ReadFromJsonAsync<ConfigurationState>();
        Assert.NotNull(changed);
        Assert.True(changed.RefreshesMetadata);

        // And it is nothing to do with the switch next to it: one moves files
        // somebody downloaded, the other rewrites files the tool wrote itself.
        Assert.False(changed.Unattended);

        var state = await client.GetFromJsonAsync<RefreshState>("/api/refresh");
        Assert.NotNull(state);
        Assert.True(state.Unattended);
    }

    /// <summary>
    /// There is no plan endpoint, and that is a decision rather than an
    /// omission: a preview stands between somebody and a move that loses a file,
    /// and this run moves nothing.
    /// </summary>
    [Fact]
    public async Task There_is_nothing_to_preview_with()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        using var response = await client.PostAsync(
            new Uri("/api/refresh/plan", UriKind.Relative),
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A check asked for before the setup is finished says why rather than
    /// answering an empty list somebody has to interpret.
    /// </summary>
    [Fact]
    public async Task A_check_before_the_setup_is_finished_says_so()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        using var started = await client.PostAsync(new Uri("/api/refresh", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        var state = await Settled(client);

        Assert.NotNull(state.Problem);
        Assert.Equal(0, state.Checked);
    }

    /// <summary>
    /// A configured library with nothing filed into it is not a problem: there
    /// is nothing to check, and the run says so in the sentence the screen shows.
    /// </summary>
    [Fact]
    public async Task A_check_over_an_empty_library_says_there_was_nothing_to_check()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        await ConfiguredAsync(client, directory);

        using var started = await client.PostAsync(new Uri("/api/refresh", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        var state = await Settled(client);

        Assert.Null(state.Problem);
        Assert.Equal(0, state.Checked);
        Assert.Equal(0, state.Sidecars);
        Assert.Contains("no scenes this tool filed", state.WhatItDid!, StringComparison.Ordinal);
    }

    /// <summary>Asking twice is the same answer — one gate over one library.</summary>
    [Fact]
    public async Task Asking_twice_is_the_same_answer()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        await ConfiguredAsync(client, directory);

        using var first = await client.PostAsync(new Uri("/api/refresh", UriKind.Relative), content: null);
        using var second = await client.PostAsync(new Uri("/api/refresh", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>Polls until the check that was started has finished.</summary>
    private static async Task<RefreshState> Settled(HttpClient client)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = await client.GetFromJsonAsync<RefreshState>("/api/refresh");

            if (state is not null && !state.Running && state.FinishedAt is not null)
            {
                return state;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException("The check did not finish.");
    }

    private static async Task ConfiguredAsync(HttpClient client, TempDirectory directory)
    {
        var downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;
        var library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        await Accepted(client.PutAsJsonAsync("/api/configuration/api-key", new SetApiKeyRequest(ApiKey)));
        await Accepted(client.PostAsJsonAsync("/api/configuration/sources", new AddSourceRequest(downloads)));
        await Accepted(client.PutAsJsonAsync(
            "/api/configuration/target",
            new SetTargetRequest(library, "Jellyfin")));
        await Accepted(client.PostAsync(new Uri("/api/configuration/completion", UriKind.Relative), content: null));
    }

    private static async Task Accepted(Task<HttpResponseMessage> call)
    {
        using var response = await call;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpClient> SignedIn(OrdenoApplication application)
    {
        var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return client;
    }
}
