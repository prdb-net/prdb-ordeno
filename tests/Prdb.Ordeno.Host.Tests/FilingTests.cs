using System.Net;
using System.Net.Http.Json;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Host.Configuration;
using Prdb.Ordeno.Host.Library;

using Xunit;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// Issue #17 over HTTP, against the application as <c>Program.cs</c> composes
/// it: the one endpoint in this tool that moves a file the user cannot get back
/// is behind the password, and it is a POST somebody makes after reading a plan.
/// </summary>
/// <remarks>
/// What a run <em>does</em> is settled in <c>Prdb.Ordeno.Infrastructure.Tests</c>,
/// where the clock can be moved by hand — a file is only a candidate once two
/// scans a quiet period apart have seen it unchanged, and ADR 0015 rules out
/// replacing the clock here. What these tests are about is the wiring: who may
/// call this, and that nothing about it happens without being asked.
/// </remarks>
public sealed class FilingTests
{
    private const string Password = "a-password-nobody-guesses";
    private const string ApiKey = "the-only-key-prdb-knows";

    /// <summary>
    /// Three ways in, all shut. The last of them moves files, which makes this
    /// the plainest case in the application for the fallback policy being what
    /// closes a door rather than somebody remembering to.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/filing")]
    [InlineData("POST", "/api/filing/plan")]
    [InlineData("POST", "/api/filing")]
    [InlineData("DELETE", "/api/filing/holds")]
    [InlineData("DELETE", "/api/filing/holds/1")]
    public async Task Filing_is_behind_the_password(string method, string path)
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var stranger = application.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        using var response = await stranger.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A container that has just started has filed nothing and planned nothing,
    /// and says so in a shape the screen can render rather than by leaving the
    /// section out.
    /// </summary>
    [Fact]
    public async Task A_fresh_installation_has_planned_nothing_and_filed_nothing()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var state = await client.GetFromJsonAsync<FilingState>("/api/filing");

        Assert.NotNull(state);
        Assert.False(state.Running);
        Assert.Null(state.PlannedAt);
        Assert.Null(state.FiledAt);
        Assert.Empty(state.Plan);
        Assert.Empty(state.Results);
        Assert.Null(state.WhatItWouldDo);
        Assert.Null(state.WhatItDid);

        // And it says which way the switch is set, because the sentence on the
        // screen is built from it — ADR 0031.
        Assert.False(state.Unattended);
        Assert.False(state.AskedByTimer);
        Assert.Equal(0, state.Held);
    }

    /// <summary>
    /// Releasing a hold that is not there is not an error: the caller asked for
    /// a file that is not held, and a file that is not held is the answer. It
    /// works out the plan again either way, because that is what the screen
    /// reads next.
    /// </summary>
    [Fact]
    public async Task Releasing_nothing_is_an_answer_rather_than_a_failure()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        using var one = await client.DeleteAsync(new Uri("/api/filing/holds/1", UriKind.Relative));
        using var all = await client.DeleteAsync(new Uri("/api/filing/holds", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.NotNull(await all.Content.ReadFromJsonAsync<FilingState>());
    }

    /// <summary>
    /// ADR 0022 as the API sees it. Working out what would happen is its own
    /// call, and it is the one the screen makes first.
    /// </summary>
    [Fact]
    public async Task Working_out_what_would_happen_is_a_call_of_its_own()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        var downloads = await ConfiguredAsync(client, directory);
        await File.WriteAllBytesAsync(Path.Combine(downloads, "video.mkv"), new byte[2048]);

        using var response = await client.PostAsync(new Uri("/api/filing/plan", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<FilingState>());
    }

    /// <summary>
    /// The claim this milestone has to keep: a video is not filed because the
    /// tool was left running. Nothing here presses the button, and the file is
    /// where it was put — including after a scan, an identification run and a
    /// plan have all happened.
    /// </summary>
    [Fact]
    public async Task Nothing_is_filed_without_being_asked_for()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        var downloads = await ConfiguredAsync(client, directory);
        var video = Path.Combine(downloads, "video.mkv");
        await File.WriteAllBytesAsync(video, new byte[2048]);

        await Accepted(client.PostAsync(new Uri("/api/scan", UriKind.Relative), content: null));
        await Accepted(client.PostAsync(new Uri("/api/identification", UriKind.Relative), content: null));
        await Accepted(client.PostAsync(new Uri("/api/filing/plan", UriKind.Relative), content: null));

        var state = await client.GetFromJsonAsync<FilingState>("/api/filing");

        Assert.NotNull(state);
        Assert.Null(state.FiledAt);
        Assert.Empty(state.Results);
        Assert.True(File.Exists(video));
    }

    /// <summary>
    /// Asking while something is under way is not an error, for the same reason
    /// a second scan is not — but here it is also the gate that stops two runs
    /// moving one file.
    /// </summary>
    [Fact]
    public async Task Asking_twice_is_the_same_answer()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        await ConfiguredAsync(client, directory);

        using var first = await client.PostAsync(new Uri("/api/filing", UriKind.Relative), content: null);
        using var second = await client.PostAsync(new Uri("/api/filing", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>
    /// A run asked for before the setup is finished says why rather than
    /// answering an empty list somebody has to interpret.
    /// </summary>
    [Fact]
    public async Task A_run_before_the_setup_is_finished_says_so()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        await Accepted(client.PostAsync(new Uri("/api/filing/plan", UriKind.Relative), content: null));

        var state = await Settled(client);

        Assert.NotNull(state.Problem);
        Assert.Empty(state.Plan);
    }

    /// <summary>Polls until the run that was started has finished.</summary>
    private static async Task<FilingState> Settled(HttpClient client)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = await client.GetFromJsonAsync<FilingState>("/api/filing");

            if (state is not null && !state.Running && state.PlannedAt is not null)
            {
                return state;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException("The filing run did not finish.");
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
