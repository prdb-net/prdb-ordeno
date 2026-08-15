using System.Net;
using System.Net.Http.Json;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Host.Configuration;

using Xunit;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// Issue #25 over HTTP: the two optional fields ADR 0018 adds to onboarding, and
/// everything they are allowed to change about a tool that works without them.
/// </summary>
public sealed class MediaServerTests
{
    private const string Password = "a-password-nobody-guesses";
    private const string ApiKey = "the-only-key-prdb-knows";
    private const string ServerKey = "the-only-key-the-media-server-knows";

    [Fact]
    public async Task The_media_server_endpoints_are_behind_the_password()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var stranger = application.CreateClient();

        var response = await stranger.PutAsJsonAsync(
            "/api/configuration/media-server",
            new SetMediaServerRequest("http://nas:8096", ServerKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The first thing ADR 0018 asks for, and the easiest to lose: a setup that
    /// never mentions a media server as something it needs. The fields are not
    /// touched, the setup finishes, and nothing anywhere says a word about what
    /// is missing — because nothing is.
    /// </summary>
    [Fact]
    public async Task A_setup_that_never_touches_it_finishes_and_says_nothing_about_one()
    {
        using var directory = new TempDirectory();
        var server = FakeMediaServer.Accepting(ServerKey);
        await using var application = new OrdenoApplication(
            directory.Root,
            prdb: FakePrdb.Accepting(ApiKey),
            mediaServer: server);
        using var client = await SignedIn(application);

        var state = await WalkTheWholePath(client, directory);

        Assert.True(state.Complete);
        Assert.Null(state.MediaServer);
        Assert.DoesNotContain("media server", state.WhatHappensNext, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, server.Calls);
    }

    /// <summary>
    /// And with the fields filled in, the setup ends on the third thing the
    /// connection buys: not "filed", but "filed, and your media server can see
    /// it".
    /// </summary>
    [Fact]
    public async Task A_connection_that_answers_is_stored_and_the_key_never_comes_back()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(
            directory.Root,
            prdb: FakePrdb.Accepting(ApiKey),
            mediaServer: FakeMediaServer.Accepting(ServerKey));
        using var client = await SignedIn(application);

        await WalkTheWholePath(client, directory);

        var check = await Connected(client, "http://nas:8096", ServerKey);

        // Nothing has been filed yet, so the path substitution is unproven — and
        // that is not a complaint, it is the ordinary state during setup.
        Assert.Equal("Unproven", check.Status);
        Assert.Contains("Jellyfin Server 10.11.11", check.Message, StringComparison.Ordinal);
        Assert.Equal("http://nas:8096/", check.Configuration.MediaServer!.Url);

        var state = await client.GetFromJsonAsync<ConfigurationState>("/api/configuration");
        Assert.Equal("http://nas:8096/", state!.MediaServer!.Url);

        // The whole state, serialised, and the key is nowhere in it.
        var body = await client.GetStringAsync(new Uri("/api/configuration", UriKind.Relative));
        Assert.DoesNotContain(ServerKey, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The failure this connection exists for. A server set to something other
    /// than <c>yyyy-MM-dd</c> discards every date the tool writes, and neither
    /// side reports anything — so the setup says it, in the one place somebody is
    /// standing in front of.
    /// </summary>
    [Fact]
    public async Task A_server_that_would_discard_every_date_says_so_during_setup()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(
            directory.Root,
            prdb: FakePrdb.Accepting(ApiKey),
            mediaServer: FakeMediaServer.Accepting(ServerKey, releaseDateFormat: "dd.MM.yyyy"));
        using var client = await SignedIn(application);

        await WalkTheWholePath(client, directory);

        var check = await Connected(client, "http://nas:8096", ServerKey);

        Assert.Equal("DatesDiscarded", check.Status);
        Assert.False(check.Working);
        Assert.Contains("'dd.MM.yyyy'", check.Message, StringComparison.Ordinal);

        // Stored all the same: what it says is something the user can go and fix,
        // and refusing the connection would not make the dates appear.
        Assert.NotNull(check.Configuration.MediaServer);
    }

    [Fact]
    public async Task A_key_the_server_refuses_is_named_as_wrong_and_not_stored()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(
            directory.Root,
            prdb: FakePrdb.Accepting(ApiKey),
            mediaServer: FakeMediaServer.Refusing());
        using var client = await SignedIn(application);

        var response = await client.PutAsJsonAsync(
            "/api/configuration/media-server",
            new SetMediaServerRequest("http://nas:8096", "wrong"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
        Assert.Contains("did not accept this API key", problem!.Message, StringComparison.Ordinal);
        Assert.Null(problem.Configuration.MediaServer);
    }

    /// <summary>
    /// A server that did not answer is not the same as a key that is wrong, and
    /// storing the pair anyway would be the tool claiming something it does not
    /// know — the same rule the prdb key is stored under.
    /// </summary>
    [Fact]
    public async Task A_server_that_did_not_answer_is_not_stored_either()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(
            directory.Root,
            prdb: FakePrdb.Accepting(ApiKey),
            mediaServer: FakeMediaServer.Unreachable());
        using var client = await SignedIn(application);

        var response = await client.PutAsJsonAsync(
            "/api/configuration/media-server",
            new SetMediaServerRequest("http://nas:8096", ServerKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
        Assert.Contains("could not be reached", problem!.Message, StringComparison.Ordinal);
        Assert.Null(problem.Configuration.MediaServer);
    }

    [Fact]
    public async Task An_address_that_is_not_one_is_refused_without_anything_being_asked()
    {
        using var directory = new TempDirectory();
        var server = FakeMediaServer.Accepting(ServerKey);
        await using var application = new OrdenoApplication(
            directory.Root,
            prdb: FakePrdb.Accepting(ApiKey),
            mediaServer: server);
        using var client = await SignedIn(application);

        var response = await client.PutAsJsonAsync(
            "/api/configuration/media-server",
            new SetMediaServerRequest("ftp://nas", ServerKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, server.Calls);
    }

    /// <summary>
    /// Two of the three things the test proves change without anybody touching
    /// this tool: a key can be revoked, and a library can be pointed elsewhere.
    /// So the test is its own endpoint rather than something that only happens
    /// once.
    /// </summary>
    [Fact]
    public async Task A_stored_connection_can_be_tested_again_afterwards()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(
            directory.Root,
            prdb: FakePrdb.Accepting(ApiKey),
            mediaServer: FakeMediaServer.Accepting(ServerKey));
        using var client = await SignedIn(application);

        await WalkTheWholePath(client, directory);
        await Connected(client, "http://nas:8096", ServerKey);

        var response = await client.PostAsync(
            new Uri("/api/configuration/media-server/test", UriKind.Relative),
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var check = await response.Content.ReadFromJsonAsync<MediaServerCheckState>();
        Assert.Equal("Unproven", check!.Status);
    }

    /// <summary>
    /// Testing what is not there is not an error the user has to fix. It is the
    /// state most installations run in, and the answer says so.
    /// </summary>
    [Fact]
    public async Task Testing_a_connection_nobody_configured_says_that_is_a_complete_setup()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var response = await client.PostAsync(
            new Uri("/api/configuration/media-server/test", UriKind.Relative),
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
        Assert.Contains("complete setup", problem!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_connection_can_be_forgotten_again()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(
            directory.Root,
            prdb: FakePrdb.Accepting(ApiKey),
            mediaServer: FakeMediaServer.Accepting(ServerKey));
        using var client = await SignedIn(application);

        await WalkTheWholePath(client, directory);
        await Connected(client, "http://nas:8096", ServerKey);

        var response = await client.DeleteAsync(
            new Uri("/api/configuration/media-server", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await response.Content.ReadFromJsonAsync<ConfigurationState>();
        Assert.Null(state!.MediaServer);
        Assert.True(state.Complete);
    }

    private static async Task<MediaServerCheckState> Connected(HttpClient client, string url, string apiKey)
    {
        var response = await client.PutAsJsonAsync(
            "/api/configuration/media-server",
            new SetMediaServerRequest(url, apiKey));

        if (response.StatusCode is not HttpStatusCode.OK)
        {
            Assert.Fail($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<MediaServerCheckState>())!;
    }

    private static async Task<ConfigurationState> WalkTheWholePath(HttpClient client, TempDirectory directory)
    {
        var downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;
        var library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        await client.PutAsJsonAsync("/api/configuration/api-key", new SetApiKeyRequest(ApiKey));
        await client.PostAsJsonAsync("/api/configuration/sources", new AddSourceRequest(downloads));
        await client.PutAsJsonAsync(
            "/api/configuration/target",
            new SetTargetRequest(library, "Jellyfin"));

        var finished = await client.PostAsync(
            new Uri("/api/configuration/completion", UriKind.Relative),
            content: null);

        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);

        return (await finished.Content.ReadFromJsonAsync<ConfigurationState>())!;
    }

    private static async Task<HttpClient> SignedIn(OrdenoApplication application)
    {
        var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return client;
    }
}
