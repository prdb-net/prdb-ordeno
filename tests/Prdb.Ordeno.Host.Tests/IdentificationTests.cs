using System.Net;
using System.Net.Http.Json;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Host.Configuration;
using Prdb.Ordeno.Host.Scanning;

using Xunit;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// Issue #15 over HTTP, against the application as it is composed: asking prdb
/// is behind the password, a run can be asked for, and what comes back is the
/// same document the downloads screen already reads.
/// </summary>
/// <remarks>
/// What a run <em>produces</em> is settled in
/// <c>Prdb.Ordeno.Infrastructure.Tests</c>, where the clock can be moved by hand.
/// A file is only asked about once two scans a quiet period apart have seen it
/// unchanged, and ADR 0015 rules out replacing the clock here — the wiring is
/// the subject of these tests, and a test that stubs its way past the wiring has
/// stopped testing it.
/// </remarks>
public sealed class IdentificationTests
{
    private const string Password = "a-password-nobody-guesses";
    private const string ApiKey = "the-only-key-prdb-knows";

    /// <summary>
    /// It spends the user's prdb quota and it reports what is in their download
    /// directories. Either would be reason enough.
    /// </summary>
    [Fact]
    public async Task Asking_prdb_is_behind_the_password()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var stranger = application.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await stranger.PostAsync(new Uri("/api/identification", UriKind.Relative), content: null))
                .StatusCode);
    }

    /// <summary>
    /// A fresh installation has nothing to ask about and says so in numbers
    /// rather than by leaving the section out.
    /// </summary>
    [Fact]
    public async Task A_fresh_installation_has_asked_about_nothing()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var state = await client.GetFromJsonAsync<ScanState>("/api/scan");

        Assert.NotNull(state);
        Assert.False(state.Identification.Running);
        Assert.Null(state.Identification.LastRunFinishedAt);
        Assert.Null(state.Identification.Problem);
        Assert.Equal(0, state.Identification.Recognised);
        Assert.Equal(0, state.Identification.Waiting);
        Assert.Equal(0, state.Identification.PerceptualBacklog);
        Assert.Null(state.Identification.WhatItRecognised);
    }

    /// <summary>
    /// The button on the screen. It answers with the state as it is now rather
    /// than holding the request open — a first pass over a library takes longer
    /// than a browser will wait.
    /// </summary>
    [Fact]
    public async Task A_run_can_be_asked_for_and_answers_with_the_screen()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        var downloads = await ConfiguredAsync(client, directory);
        await File.WriteAllBytesAsync(Path.Combine(downloads, "video.mkv"), new byte[2048]);

        using var response = await client.PostAsync(new Uri("/api/identification", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await response.Content.ReadFromJsonAsync<ScanState>();

        Assert.NotNull(state);
        Assert.True(state.OnboardingComplete);

        // The file has only just arrived, so there is nothing to ask about yet
        // and nothing was asked. What matters here is that saying so is a
        // finished answer rather than an error.
        Assert.Null(state.Identification.Problem);
    }

    /// <summary>
    /// Asking twice is not an error, for the same reason a second scan is not:
    /// the answer to "identify now" is a run in progress either way.
    /// </summary>
    [Fact]
    public async Task Asking_twice_is_the_same_answer()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        await ConfiguredAsync(client, directory);

        using var first = await client.PostAsync(new Uri("/api/identification", UriKind.Relative), content: null);
        using var second = await client.PostAsync(new Uri("/api/identification", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>A finished onboarding, walked the way a user walks it.</summary>
    private static async Task<string> ConfiguredAsync(HttpClient client, TempDirectory directory)
    {
        var downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;
        var library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        await Accepted(client.PutAsJsonAsync("/api/configuration/api-key", new SetApiKeyRequest(ApiKey)));
        await Accepted(client.PostAsJsonAsync("/api/configuration/sources", new AddSourceRequest(downloads)));
        await Accepted(client.PutAsJsonAsync(
            "/api/configuration/target",
            new SetTargetRequest(library, "Jellyfin")));
        await Accepted(client.PostAsync(new Uri("/api/configuration/completion", UriKind.Relative), content: null));

        return downloads;
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
