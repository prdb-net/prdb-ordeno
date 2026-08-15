using System.Net;
using System.Net.Http.Json;

using Prdb.Ordeno.Host.Access;
using Prdb.Ordeno.Host.Configuration;
using Prdb.Ordeno.Host.Review;

using Xunit;

namespace Prdb.Ordeno.Host.Tests;

/// <summary>
/// Issue #16 over HTTP, against the application as <c>Program.cs</c> composes
/// it: who may work the queue, and what the answers look like when prdb will not
/// help.
/// </summary>
/// <remarks>
/// What the queue <em>holds</em> is settled in
/// <c>Prdb.Ordeno.Infrastructure.Tests</c>, where the clock can be moved by hand
/// — a file only reaches the queue once two scans a quiet period apart have seen
/// it unchanged, and ADR 0015 rules out replacing the clock here. These tests are
/// about the wiring.
/// </remarks>
public sealed class ReviewTests
{
    private const string Password = "a-password-nobody-guesses";
    private const string ApiKey = "the-only-key-prdb-knows";

    /// <summary>
    /// Every way in, all shut. Two of these spend the user's prdb quota and the
    /// rest decide what the next filing run acts on, so a stranger reaching any
    /// of them is a stranger deciding what happens to somebody's library.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/queue", null)]
    [InlineData("GET", "/api/queue/search?q=anything", null)]
    [InlineData("POST", "/api/queue/1/assignment", """{"videoId":"6f9619ff-8b86-d011-b42d-00c04fc964ff"}""")]
    [InlineData("POST", "/api/queue/1/dismissal", null)]
    [InlineData("POST", "/api/queue/dismissals", """{"fileIds":[1]}""")]
    [InlineData("DELETE", "/api/queue/1/decision", null)]
    public async Task The_queue_is_behind_the_password(string method, string path, string? body)
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var stranger = application.CreateClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        // The two that take a body get one. Without it they are not the endpoint
        // being asked about at all — routing turns a request with no JSON in it
        // away before the door this test is checking.
        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await stranger.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A container that has just started has nothing waiting, and says so in a
    /// sentence rather than by answering an empty list somebody has to interpret.
    /// </summary>
    [Fact]
    public async Task A_fresh_installation_has_nothing_waiting()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var queue = await client.GetFromJsonAsync<ReviewQueueState>("/api/queue");

        Assert.NotNull(queue);
        Assert.Equal("waiting", queue.Filter);
        Assert.Empty(queue.Entries);
        Assert.Equal(0, queue.Total);
        Assert.Equal(1, queue.Page);
        Assert.Equal(0, queue.Summary.Waiting);
        Assert.NotEmpty(queue.Summary.WhatIsWaiting);
    }

    /// <summary>
    /// The three lists are one screen with a filter, and a name the tool does not
    /// know is not worth refusing: the question was "show me the queue".
    /// </summary>
    [Theory]
    [InlineData("assigned", "assigned")]
    [InlineData("dismissed", "dismissed")]
    [InlineData("nonsense", "waiting")]
    public async Task The_filter_is_a_name_in_the_query_string(string asked, string answered)
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root);
        using var client = await SignedIn(application);

        var queue = await client.GetFromJsonAsync<ReviewQueueState>($"/api/queue?filter={asked}");

        Assert.Equal(answered, queue?.Filter);
    }

    /// <summary>
    /// prdb refusing is a line under the search box, not a failed call. The
    /// screen has to be able to show what went wrong next to the field somebody
    /// typed in.
    /// </summary>
    [Fact]
    public async Task A_search_prdb_refuses_answers_with_what_went_wrong()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(
            directory.Root,
            prdb: FakePrdb.AcceptingOnlyTheKeyCheck(ApiKey));

        using var client = await SignedIn(application);
        await ConfiguredAsync(client, directory);

        var found = await client.GetFromJsonAsync<VideoSearchState>("/api/queue/search?q=a+scene");

        Assert.NotNull(found);
        Assert.False(found.Answered);
        Assert.Empty(found.Videos);
        Assert.Contains("subscription", found.Problem!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing typed is not a question, and it must not become a request against
    /// somebody's quota.
    /// </summary>
    [Fact]
    public async Task An_empty_search_asks_prdb_nothing()
    {
        using var directory = new TempDirectory();
        var prdb = FakePrdb.Accepting(ApiKey);
        await using var application = new OrdenoApplication(directory.Root, prdb: prdb);

        using var client = await SignedIn(application);
        await ConfiguredAsync(client, directory);

        var asked = prdb.Calls;
        var found = await client.GetFromJsonAsync<VideoSearchState>("/api/queue/search?q=");

        Assert.NotNull(found);
        Assert.True(found.Answered);
        Assert.Empty(found.Videos);
        Assert.Equal(asked, prdb.Calls);
    }

    /// <summary>
    /// A decision about a file that is not in a download directory any more is
    /// refused with the reason, and the refusal still carries the counts — the
    /// screen has to show a message and stay true to what is stored.
    /// </summary>
    [Fact]
    public async Task Deciding_about_a_file_that_is_gone_is_refused_with_the_counts()
    {
        using var directory = new TempDirectory();
        await using var application = new OrdenoApplication(directory.Root, prdb: FakePrdb.Accepting(ApiKey));

        using var client = await SignedIn(application);
        await ConfiguredAsync(client, directory);

        using var response = await client.PostAsJsonAsync(
            "/api/queue/4242/assignment",
            new AssignRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var decision = await response.Content.ReadFromJsonAsync<ReviewDecisionState>();

        Assert.NotNull(decision);
        Assert.False(decision.Made);
        Assert.Null(decision.Entry);
        Assert.NotNull(decision.Problem);
        Assert.Equal(0, decision.Summary.Waiting);
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
