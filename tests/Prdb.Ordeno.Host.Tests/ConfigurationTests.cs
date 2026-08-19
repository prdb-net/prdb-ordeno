using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Host.Configuration;

using Xunit;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// Issue #5 over HTTP: nothing is stored before it has been checked, the API key
/// never comes back out, and the guided path ends by saying what the tool will
/// do. The application is the real one, and so is the filesystem it inspects —
/// the directories in these tests exist, or deliberately do not.
/// </summary>
public sealed class ConfigurationTests
{
    private const string Password = "a-password-nobody-guesses";
    private const string ApiKey = "the-only-key-prdb-knows";

    [Fact]
    public async Task The_configuration_is_behind_the_password()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var stranger = application.CreateClient();

        var response = await stranger.GetAsync(new Uri("/api/configuration", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_fresh_installation_is_configured_with_nothing_and_says_what_it_needs()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var state = await client.GetFromJsonAsync<ConfigurationState>("/api/configuration");

        Assert.NotNull(state);
        Assert.False(state.ApiKeySet);
        Assert.Empty(state.Sources);
        Assert.Null(state.Target);
        Assert.False(state.Complete);
        Assert.False(state.ReadyToComplete);
        Assert.Contains("Nothing is scanned yet", state.WhatHappensNext, StringComparison.Ordinal);

        // ADR 0008: one layout, and the UI is told which rather than guessing.
        Assert.Equal("Jellyfin", Assert.Single(state.AvailableLayouts).Name);
    }

    /// <summary>
    /// The whole reason the key is checked here: a key that does not work is a
    /// message next to the field, not a failure discovered on the first scan.
    /// </summary>
    [Fact]
    public async Task A_key_prdb_refuses_is_named_as_wrong_and_not_stored()
    {
        using var directory = new TempDirectory();
        var prdb = FakePrdb.Refusing();
        await using var application = new OrdenoApplication(directory.Root, prdb: prdb);
        using var client = await SignedIn(application);

        var response = await client.PutAsJsonAsync("/api/configuration/api-key", new SetApiKeyRequest("wrong"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
        Assert.Contains("prdb does not know this key", problem!.Message, StringComparison.Ordinal);
        Assert.False(problem.Configuration.ApiKeySet);
        Assert.Equal(1, prdb.Calls);
    }

    /// <summary>
    /// prdb being down is not the same as the key being wrong, and storing it
    /// anyway would be the tool claiming something it does not know.
    /// </summary>
    [Fact]
    public async Task A_key_that_could_not_be_checked_is_not_stored_either()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Unreachable());
        using var client = await SignedIn(application);

        var response = await client.PutAsJsonAsync("/api/configuration/api-key", new SetApiKeyRequest(ApiKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
        Assert.Contains("could not be reached", problem!.Message, StringComparison.Ordinal);
        Assert.False(problem.Configuration.ApiKeySet);
    }

    [Fact]
    public async Task A_key_prdb_accepts_is_stored_and_never_comes_back_out()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        var response = await client.PutAsJsonAsync("/api/configuration/api-key", new SetApiKeyRequest(ApiKey));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await response.Content.ReadFromJsonAsync<ConfigurationState>();
        Assert.True(state!.ApiKeySet);

        // Read as text rather than as the type, so that a field added later
        // carrying the key would fail this rather than be quietly ignored.
        var configuration = await client.GetStringAsync(new Uri("/api/configuration", UriKind.Relative));
        Assert.DoesNotContain(ApiKey, configuration, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_download_directory_that_is_not_mounted_is_refused_and_named()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);
        var missing = directory.Combine("never-mounted");

        var response = await client.PostAsJsonAsync("/api/configuration/sources", new AddSourceRequest(missing));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
        Assert.Contains(missing, problem!.Message, StringComparison.Ordinal);
        Assert.Empty(problem.Configuration.Sources);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_library_directory_that_cannot_be_written_to_is_refused_and_named()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var readOnly = Directory.CreateDirectory(directory.Combine("library")).FullName;
        File.SetUnixFileMode(readOnly, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            if (CanStillWrite(readOnly))
            {
                // The tests are running as a user the permission bits do not
                // apply to, root in a container being the usual reason. There is
                // nothing here to observe then.
                return;
            }

            var response = await client.PutAsJsonAsync(
                "/api/configuration/target",
                new SetTargetRequest(readOnly, "Jellyfin"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
            Assert.Contains(readOnly, problem!.Message, StringComparison.Ordinal);
            Assert.Contains("PUID", problem.Message, StringComparison.Ordinal);
            Assert.Null(problem.Configuration.Target);
        }
        finally
        {
            File.SetUnixFileMode(
                readOnly,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// ADR 0027's switch, over HTTP: off on a fresh installation, and on only
    /// because somebody asked. It is not part of the guided path, so nothing
    /// about finishing the setup depends on it.
    /// </summary>
    [Fact]
    public async Task Artwork_is_off_until_it_is_turned_on()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var fresh = await client.GetFromJsonAsync<ConfigurationState>("/api/configuration");
        Assert.False(fresh!.Artwork);

        var response = await client.PutAsJsonAsync(
            "/api/configuration/artwork",
            new SetArtworkRequest(Enabled: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<ConfigurationState>())!.Artwork);

        // And back off again, because a switch that only goes one way is not one.
        var off = await client.PutAsJsonAsync(
            "/api/configuration/artwork",
            new SetArtworkRequest(Enabled: false));

        Assert.False((await off.Content.ReadFromJsonAsync<ConfigurationState>())!.Artwork);
    }

    [Fact]
    public async Task A_layout_this_release_does_not_have_is_refused()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);
        var library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        var response = await client.PutAsJsonAsync(
            "/api/configuration/target",
            new SetTargetRequest(library, "Plex"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
        Assert.Contains("Jellyfin", problem!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A library inside the downloads, or the other way round, means the tool
    /// finds what it has just filed and files it again.
    /// </summary>
    [Fact]
    public async Task A_library_inside_a_download_directory_is_refused()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        var downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;
        var inside = Directory.CreateDirectory(Path.Combine(downloads, "library")).FullName;

        await client.PostAsJsonAsync("/api/configuration/sources", new AddSourceRequest(downloads));

        var response = await client.PutAsJsonAsync(
            "/api/configuration/target",
            new SetTargetRequest(inside, "Jellyfin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
        Assert.Contains("inside one another", problem!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finishing_is_refused_while_anything_is_missing()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        await client.PutAsJsonAsync("/api/configuration/api-key", new SetApiKeyRequest(ApiKey));

        var response = await client.PostAsync(new Uri("/api/configuration/completion", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ConfigurationProblem>();
        Assert.Contains("downloads arrive in", problem!.Message, StringComparison.Ordinal);
        Assert.False(problem.Configuration.Complete);
    }

    [Fact]
    public async Task The_guided_path_ends_by_saying_what_the_tool_will_do()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        var state = await WalkTheWholePath(client, directory);

        Assert.True(state.Complete);
        Assert.True(state.ApiKeySet);
        Assert.Contains("is watching", state.WhatHappensNext, StringComparison.Ordinal);
        Assert.Contains("Jellyfin", state.WhatHappensNext, StringComparison.Ordinal);

        // Both directories are under one temporary root, so this is the fast
        // path — and the user is told which path they are on before a single
        // video has moved (ADR 0002).
        var source = Assert.Single(state.Sources);
        Assert.Equal("Rename", source.Movement);
        Assert.Contains("instant", source.MovementExplained, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_download_directory_can_be_taken_off_the_list_again()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);
        var downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;

        var added = await client.PostAsJsonAsync("/api/configuration/sources", new AddSourceRequest(downloads));
        var state = await added.Content.ReadFromJsonAsync<ConfigurationState>();
        var id = Assert.Single(state!.Sources).Id;

        var removed = await client.DeleteAsync(new Uri($"/api/configuration/sources/{id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Empty((await removed.Content.ReadFromJsonAsync<ConfigurationState>())!.Sources);
    }

    /// <summary>
    /// ADR 0009 puts the configuration in the database precisely so that this
    /// holds: the container is cattle, the data volume is not.
    /// </summary>
    [Fact]
    public async Task Restarting_the_container_keeps_everything()
    {
        using var directory = new TempDirectory();
        string library;

        await using (var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey)))
        {
            using var client = await SignedIn(application);
            library = (await WalkTheWholePath(client, directory)).Target!.Path;
        }

        await using var restarted = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var afterwards = restarted.CreateClient();

        // The password survived too, so this is a sign-in rather than a setup.
        var signIn = await afterwards.PostAsJsonAsync("/api/access/session", new SignInRequest(Password));
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        var state = await afterwards.GetFromJsonAsync<ConfigurationState>("/api/configuration");

        Assert.True(state!.Complete);
        Assert.True(state.ApiKeySet);
        Assert.Equal(library, state.Target!.Path);
        Assert.Equal("Jellyfin", state.Layout);
        Assert.Single(state.Sources);
    }

    /// <summary>
    /// Key, one download directory, the library and its layout, then finish —
    /// the path a fresh container walks a user through.
    /// </summary>
    private static async Task<ConfigurationState> WalkTheWholePath(HttpClient client, TempDirectory directory)
    {
        var downloads = Directory.CreateDirectory(directory.Combine("downloads")).FullName;
        var library = Directory.CreateDirectory(directory.Combine("library")).FullName;

        await Accepted(client.PutAsJsonAsync("/api/configuration/api-key", new SetApiKeyRequest(ApiKey)));
        await Accepted(client.PostAsJsonAsync("/api/configuration/sources", new AddSourceRequest(downloads)));
        await Accepted(client.PutAsJsonAsync(
            "/api/configuration/target",
            new SetTargetRequest(library, "Jellyfin")));

        var finished = await Accepted(
            client.PostAsync(new Uri("/api/configuration/completion", UriKind.Relative), content: null));

        return (await finished.Content.ReadFromJsonAsync<ConfigurationState>())!;
    }

    private static async Task<HttpResponseMessage> Accepted(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (response.StatusCode is HttpStatusCode.OK)
        {
            return response;
        }

        // A step that was refused says why, and that sentence is far more use in
        // the failure than "expected OK, got BadRequest".
        Assert.Fail($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        return response;
    }

    private static async Task<HttpClient> SignedIn(OrdenoApplication application)
    {
        var client = application.CreateClient();

        var response = await client.PostAsJsonAsync("/api/access/password", new SetPasswordRequest(Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return client;
    }

    private static bool CanStillWrite(string path)
    {
        try
        {
            var probe = Path.Combine(path, "probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
