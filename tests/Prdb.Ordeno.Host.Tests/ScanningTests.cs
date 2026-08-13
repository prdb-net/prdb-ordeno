using System.Net;
using System.Net.Http.Json;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Host.Configuration;
using Prdb.Ordeno.Host.Scanning;

using Xunit;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// Issue #14 over HTTP, against the application as it is composed: what is in
/// someone's download directories is behind the password, a scan can be asked
/// for, and what comes back says what was found without claiming anything was
/// done with it.
/// </summary>
public sealed class ScanningTests
{
    private const string Password = "a-password-nobody-guesses";
    private const string ApiKey = "the-only-key-prdb-knows";

    [Fact]
    public async Task What_is_in_the_download_directories_is_behind_the_password()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var stranger = application.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await stranger.GetAsync(new Uri("/api/scan", UriKind.Relative))).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await stranger.PostAsync(new Uri("/api/scan", UriKind.Relative), content: null)).StatusCode);
    }

    /// <summary>
    /// ADR 0009: a fresh container scans nothing and says so, rather than
    /// showing an empty list that reads as "there is nothing there".
    /// </summary>
    [Fact]
    public async Task A_fresh_installation_says_the_setup_comes_first()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var state = await client.GetFromJsonAsync<ScanState>("/api/scan");

        Assert.NotNull(state);
        Assert.False(state.OnboardingComplete);
        Assert.False(state.Scanning);
        Assert.Null(state.LastScanStartedAt);
        Assert.Empty(state.Files);
        Assert.Contains("Finish the setup", state.WhatItFound, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_scan_finds_the_videos_and_leaves_them_where_they_are()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        var downloads = await ConfiguredAsync(client, directory);

        await File.WriteAllBytesAsync(Path.Combine(downloads, "video.mkv"), new byte[2048]);
        await File.WriteAllBytesAsync(Path.Combine(downloads, "still-arriving.mkv.part"), new byte[2048]);

        var state = await ScannedAsync(client);

        Assert.Equal(1, state.Total);
        var file = Assert.Single(state.Files);
        Assert.Equal("video.mkv", file.Name);
        Assert.Equal(2048, file.SizeBytes);

        // Just arrived, so it has not settled yet — and the screen says as much
        // rather than presenting it as something about to be dealt with.
        Assert.False(file.Ready);
        Assert.Equal(1, state.Settling);

        // The one thing this milestone must not do.
        Assert.True(File.Exists(Path.Combine(downloads, "video.mkv")));
        Assert.True(File.Exists(Path.Combine(downloads, "still-arriving.mkv.part")));
    }

    /// <summary>
    /// The sentence someone reads to find out whether their downloads are being
    /// dealt with. It must not let them conclude that they are.
    /// </summary>
    [Fact]
    public async Task What_was_found_does_not_claim_anything_was_filed()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        var downloads = await ConfiguredAsync(client, directory);
        await File.WriteAllBytesAsync(Path.Combine(downloads, "video.mkv"), new byte[2048]);

        var state = await ScannedAsync(client);

        Assert.Contains("Nothing is filed yet", state.WhatItFound, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asking twice is not an error. The answer to "scan now" is a scan in
    /// progress, whether this request started it or the clock did.
    /// </summary>
    [Fact]
    public async Task Asking_for_a_scan_while_one_is_running_is_the_same_answer()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        await ConfiguredAsync(client, directory);

        var first = await client.PostAsync(new Uri("/api/scan", UriKind.Relative), content: null);
        var second = await client.PostAsync(new Uri("/api/scan", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>
    /// Configuration and the inventory are the same directories seen from two
    /// sides, and a directory that has gone away has to say so on both.
    /// </summary>
    [Fact]
    public async Task A_directory_that_has_gone_away_is_reported_as_unreadable()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        var downloads = await ConfiguredAsync(client, directory);
        Directory.Delete(downloads, recursive: true);

        var state = await client.GetFromJsonAsync<ScanState>("/api/scan");

        var source = Assert.Single(state!.Sources);
        Assert.False(source.Reachable);
        Assert.NotNull(source.Problem);
        Assert.Contains("cannot be read", state.WhatItFound, StringComparison.Ordinal);
    }

    /// <summary>Starts a scan and waits for the one that started to finish.</summary>
    private static async Task<ScanState> ScannedAsync(HttpClient client)
    {
        var started = await client.PostAsJsonAsync("/api/scan", new { });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        // The scan outlives the request that asked for it, so the state is
        // polled rather than assumed — the same thing the screen does.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = await client.GetFromJsonAsync<ScanState>("/api/scan");

            if (state is { Scanning: false, LastScanFinishedAt: not null })
            {
                return state;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException("The scan did not finish.");
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
