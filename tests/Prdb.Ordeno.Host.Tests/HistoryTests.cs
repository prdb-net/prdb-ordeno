using System.Net;
using System.Net.Http.Json;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Host.Configuration;
using Prdb.Ordeno.Host.History;

using Xunit;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// Issue #19 over HTTP, against the application as <c>Program.cs</c> composes
/// it: the log is behind the password, and so is every way of moving a file back
/// out of the library.
/// </summary>
/// <remarks>
/// What an undo <em>does</em> is settled in
/// <c>Prdb.Ordeno.Infrastructure.Tests</c>, where the clock can be moved by hand
/// and a file can be made to change under the tool's feet. What these tests are
/// about is the wiring: who may call this, and that a check is a call of its own.
/// </remarks>
public sealed class HistoryTests
{
    private const string Password = "a-password-nobody-guesses";
    private const string ApiKey = "the-only-key-prdb-knows";

    /// <summary>
    /// Six ways in, all shut. Four of them move files, which makes this the same
    /// plain case filing is for the fallback policy being what closes a door
    /// rather than somebody remembering to.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/history")]
    [InlineData("GET", "/api/history/undo")]
    [InlineData("POST", "/api/history/runs/1/undo/check")]
    [InlineData("POST", "/api/history/runs/1/undo")]
    [InlineData("POST", "/api/history/operations/1/undo/check")]
    [InlineData("POST", "/api/history/operations/1/undo")]
    public async Task The_log_and_the_way_back_are_behind_the_password(string method, string path)
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var stranger = application.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        using var response = await stranger.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A container that has just started has done nothing to anybody's files, and
    /// says so in a shape the screen can render rather than by leaving the
    /// section out.
    /// </summary>
    [Fact]
    public async Task A_fresh_installation_has_nothing_in_the_log()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var log = await client.GetFromJsonAsync<HistoryState>("/api/history");

        Assert.NotNull(log);
        Assert.Empty(log.Runs);
        Assert.Equal(0, log.Total);

        var undo = await client.GetFromJsonAsync<UndoState>("/api/history/undo");

        Assert.NotNull(undo);
        Assert.False(undo.Running);
        Assert.Null(undo.CheckedAt);
        Assert.Null(undo.UndoneAt);
        Assert.Null(undo.WhatItWouldDo);
        Assert.Null(undo.WhatItDid);
    }

    /// <summary>
    /// A run somebody asked for is in the log afterwards, whether or not it moved
    /// anything — which is the answer somebody who was asleep wants first.
    /// </summary>
    [Fact]
    public async Task A_run_that_moved_nothing_is_in_the_log()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        await ConfiguredAsync(client, directory);
        await Accepted(client.PostAsync(new Uri("/api/filing", UriKind.Relative), content: null));

        var log = await Filed(client);
        var run = Assert.Single(log.Runs);

        Assert.Equal("filing", run.Kind);
        Assert.Equal(0, run.Operations);
        Assert.False(run.CanBeUndone);
    }

    /// <summary>
    /// ADR 0029 as the API sees it: what putting a run back would do is its own
    /// call, and it is the one the screen makes first.
    /// </summary>
    [Fact]
    public async Task Checking_what_putting_it_back_would_do_is_a_call_of_its_own()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));
        using var client = await SignedIn(application);

        await ConfiguredAsync(client, directory);
        await Accepted(client.PostAsync(new Uri("/api/filing", UriKind.Relative), content: null));

        var run = Assert.Single((await Filed(client)).Runs);

        using var response = await client.PostAsync(
            new Uri($"/api/history/runs/{run.Id}/undo/check", UriKind.Relative),
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await Settled(client);

        Assert.Equal(run.Id, state.RunId);
        Assert.NotNull(state.CheckedAt);
        Assert.Null(state.UndoneAt);
    }

    /// <summary>
    /// A run that is not in the log — trimmed, or never there — says so rather
    /// than answering an empty list somebody has to interpret as either "nothing
    /// to do" or "gone".
    /// </summary>
    [Fact]
    public async Task An_undo_of_a_run_that_is_not_there_says_so()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        await Accepted(client.PostAsync(
            new Uri("/api/history/runs/404/undo/check", UriKind.Relative),
            content: null));

        var state = await Settled(client);

        Assert.NotNull(state.Problem);
        Assert.Empty(state.Plan);
    }

    /// <summary>
    /// Asking while something is under way is not an error, and here it is also
    /// the gate that stops an undo and a filing rearranging one library at once.
    /// </summary>
    [Fact]
    public async Task Asking_twice_is_the_same_answer()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        using var first = await client.PostAsync(
            new Uri("/api/history/runs/1/undo/check", UriKind.Relative),
            content: null);

        using var second = await client.PostAsync(
            new Uri("/api/history/runs/1/undo/check", UriKind.Relative),
            content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>Polls until the run that was started has finished.</summary>
    private static async Task<UndoState> Settled(HttpClient client)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = await client.GetFromJsonAsync<UndoState>("/api/history/undo");

            if (state is not null && !state.Running && state.CheckedAt is not null)
            {
                return state;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException("The undo did not finish.");
    }

    /// <summary>Polls until a filing run has left its row in the log.</summary>
    private static async Task<HistoryState> Filed(HttpClient client)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var log = await client.GetFromJsonAsync<HistoryState>("/api/history");

            if (log is not null && log.Runs.Any(run => run.FinishedAt is not null))
            {
                return log;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException("The filing run did not reach the log.");
    }

    /// <summary>A finished onboarding, walked the way a user walks it.</summary>
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
