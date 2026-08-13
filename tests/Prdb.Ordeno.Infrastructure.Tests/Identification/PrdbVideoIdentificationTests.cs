using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Prdb.Ordeno.Core.Identification;
using Prdb.Ordeno.Infrastructure.Configuration;
using Prdb.Ordeno.Infrastructure.Identification;

using Xunit;

namespace Prdb.Ordeno.Infrastructure.Tests.Identification;

/// <summary>
/// The request that goes to prdb and the answer that comes back, with the SDK in
/// between doing its real work — only the socket is replaced.
/// </summary>
/// <remarks>
/// This is where the parts nobody can check by reading are checked: that the
/// batch is shaped the way the endpoint documents, that the numbers it answers
/// with mean what this build thinks they mean, and that a refusal arrives as a
/// refusal rather than as an exception somewhere up the stack.
/// </remarks>
public sealed class PrdbVideoIdentificationTests
{
    private const string ApiKey = "the-only-key-prdb-knows";

    [Fact]
    public async Task The_question_carries_the_name_the_size_and_the_hashes()
    {
        JsonDocument? sent = null;

        var identification = Against((request, body) =>
        {
            sent = JsonDocument.Parse(body);

            Assert.Equal(ApiKey, request.Headers.GetValues("X-Api-Key").Single());

            return Answer("""{"results":[{"ref":"7","confidence":0,"candidates":[]}]}""");
        });

        await identification.IdentifyAsync(
            ApiKey,
            [new FileToIdentify("7", "video.mkv", 4096, "abcdef0123456789", "fedcba9876543210")]);

        var root = sent!.RootElement;

        // Two hundred full video documents is a large answer, and asking for
        // them is a deliberate trade — the alternative is a second request every
        // time somebody opens the screen.
        Assert.True(root.GetProperty("includeVideoDetails").GetBoolean());

        var file = root.GetProperty("files").EnumerateArray().Single();
        Assert.Equal("7", file.GetProperty("ref").GetString());
        Assert.Equal("video.mkv", file.GetProperty("filename").GetString());
        Assert.Equal(4096, file.GetProperty("filesize").GetInt64());

        // Uppercase, which is the form prdb stores: the match on its side is
        // then byte for byte rather than a favour granted by a collation.
        Assert.Equal("ABCDEF0123456789", file.GetProperty("osHash").GetString());
        Assert.Equal("FEDCBA9876543210", file.GetProperty("pHash").GetString());
    }

    [Fact]
    public async Task A_recognised_video_arrives_with_what_the_screen_needs()
    {
        var videoId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        var identification = Against((_, _) => Answer($$"""
            {
              "results": [
                {
                  "ref": "7",
                  "confidence": 4,
                  "matchedBy": 0,
                  "videoId": "{{videoId}}",
                  "video": {
                    "id": "{{videoId}}",
                    "title": "A Scene",
                    "releaseDate": "2024-05-01",
                    "createdAtUtc": "2024-05-02T00:00:00Z",
                    "updatedAtUtc": "2024-05-02T00:00:00Z",
                    "site": { "id": "{{siteId}}", "title": "A Site", "url": "https://example.test" },
                    "images": [],
                    "preNames": [],
                    "actors": []
                  },
                  "candidates": []
                }
              ]
            }
            """));

        var answer = await identification.IdentifyAsync(ApiKey, [Asked]);

        Assert.True(answer.Answered);

        var result = Assert.Single(answer.Results);
        Assert.Equal("7", result.Ref);
        Assert.Equal(MatchConfidence.Exact, result.Confidence);
        Assert.Equal(MatchRung.OsHash, result.MatchedBy);
        Assert.Equal(videoId, result.VideoId);
        Assert.Equal("A Scene", result.Title);
        Assert.Equal(new DateOnly(2024, 5, 1), result.ReleaseDate);
        Assert.Equal(siteId, result.SiteId);
        Assert.Equal("A Site", result.SiteTitle);
    }

    /// <summary>
    /// ADR 0001: prdb names the candidates and declines to choose. The answer has
    /// to survive with no video on it.
    /// </summary>
    [Fact]
    public async Task An_ambiguous_answer_keeps_its_candidates_and_names_no_video()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var identification = Against((_, _) => Answer($$"""
            {"results":[{"ref":"7","confidence":5,"matchedBy":3,"candidates":["{{first}}","{{second}}"]}]}
            """));

        var result = Assert.Single((await identification.IdentifyAsync(ApiKey, [Asked])).Results);

        Assert.Equal(MatchConfidence.Ambiguous, result.Confidence);
        Assert.Null(result.VideoId);
        Assert.Equal([first, second], result.Candidates);
    }

    /// <summary>
    /// The site rung answers with a site and no video, and that is a result
    /// rather than a hole in the answer.
    /// </summary>
    [Fact]
    public async Task A_site_only_answer_keeps_the_site()
    {
        var siteId = Guid.NewGuid();

        var identification = Against((_, _) => Answer($$"""
            {"results":[{"ref":"7","confidence":1,"matchedBy":4,
             "site":{"id":"{{siteId}}","title":"A Site","url":"https://example.test"},"candidates":[]}]}
            """));

        var result = Assert.Single((await identification.IdentifyAsync(ApiKey, [Asked])).Results);

        Assert.Equal(MatchRung.Site, result.MatchedBy);
        Assert.Equal("A Site", result.SiteTitle);
        Assert.Null(result.VideoId);
    }

    /// <summary>
    /// A rung this build has no name for is a newer prdb, not a broken one. The
    /// answer still arrives; only the explanation is missing.
    /// </summary>
    [Fact]
    public async Task A_rung_this_build_does_not_know_is_survivable()
    {
        var videoId = Guid.NewGuid();

        var identification = Against((_, _) => Answer($$"""
            {"results":[{"ref":"7","confidence":3,"matchedBy":97,"videoId":"{{videoId}}","candidates":[]}]}
            """));

        var result = Assert.Single((await identification.IdentifyAsync(ApiKey, [Asked])).Results);

        Assert.Null(result.MatchedBy);
        Assert.Equal(videoId, result.VideoId);
    }

    [Fact]
    public async Task A_key_prdb_refuses_stops_the_run_and_says_which_key()
    {
        var identification = Against((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = Json("""{"title":"Unauthorized","status":401,"detail":"Unknown API key."}"""),
        });

        var answer = await identification.IdentifyAsync(ApiKey, [Asked]);

        Assert.Equal(IdentificationStatus.Refused, answer.Status);
        Assert.Empty(answer.Results);
        Assert.Contains("API key", answer.Message!, StringComparison.Ordinal);
        Assert.NotNull(answer.RetryAfter);
    }

    /// <summary>
    /// The quota being spent is not an error, and the wait comes from prdb rather
    /// than from a number invented here.
    /// </summary>
    [Fact]
    public async Task A_spent_quota_is_a_wait_and_not_a_failure()
    {
        var identification = Against((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = Json("""{"title":"Too Many Requests","status":429}"""),
            };

            response.Headers.Add("X-RateLimit-Limit-Hour", "100");
            response.Headers.Add("X-RateLimit-Remaining-Hour", "0");
            response.Headers.Add("X-RateLimit-Reset-Hour", "900");

            return response;
        });

        var answer = await identification.IdentifyAsync(ApiKey, [Asked]);

        Assert.Equal(IdentificationStatus.RateLimited, answer.Status);
        Assert.Equal(TimeSpan.FromSeconds(900), answer.RetryAfter);
    }

    /// <summary>
    /// Pacing costs nothing, because every answer already carries what is left.
    /// </summary>
    [Fact]
    public async Task What_is_left_of_the_quota_comes_back_with_the_answer()
    {
        var identification = Against((_, _) =>
        {
            var response = Answer("""{"results":[{"ref":"7","confidence":0,"candidates":[]}]}""");

            response.Headers.Add("X-RateLimit-Limit-Hour", "100");
            response.Headers.Add("X-RateLimit-Remaining-Hour", "42");
            response.Headers.Add("X-RateLimit-Reset-Hour", "60");

            return response;
        });

        var answer = await identification.IdentifyAsync(ApiKey, [Asked]);

        Assert.Equal(42, answer.RateLimit?.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(60), answer.RateLimit?.ResetIn);
    }

    [Fact]
    public async Task prdb_not_answering_at_all_is_a_stop_rather_than_an_exception()
    {
        var identification = Against((_, _) => throw new HttpRequestException("Name or service not known."));

        var answer = await identification.IdentifyAsync(ApiKey, [Asked]);

        Assert.Equal(IdentificationStatus.Unreachable, answer.Status);
        Assert.Empty(answer.Results);
    }

    /// <summary>The endpoint takes two hundred at a time, and that is a limit the caller keeps.</summary>
    [Fact]
    public async Task A_batch_larger_than_the_endpoint_allows_is_a_mistake_in_this_repository()
    {
        var identification = Against((_, _) => Answer("""{"results":[]}"""));

        var files = Enumerable
            .Range(0, IdentificationSchedule.MaxBatch + 1)
            .Select(index => Asked with { Ref = index.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            .ToList();

        await Assert.ThrowsAsync<ArgumentException>(() => identification.IdentifyAsync(ApiKey, files));
    }

    private static FileToIdentify Asked => new("7", "video.mkv", 4096, "abcdef0123456789", null);

    private static HttpResponseMessage Answer(string body) =>
        new(HttpStatusCode.OK) { Content = Json(body) };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    /// <summary>
    /// The real client over a socket that answers as the test says. The transport
    /// comes out of an <c>IHttpMessageHandlerFactory</c> because that is where
    /// the application's comes from too.
    /// </summary>
    private static PrdbVideoIdentification Against(
        Func<HttpRequestMessage, string, HttpResponseMessage> answer)
    {
        var services = new ServiceCollection();

        services
            .AddHttpClient(PrdbTransport.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new FakeSocket(answer));

        return new PrdbVideoIdentification(
            services.BuildServiceProvider().GetRequiredService<IHttpMessageHandlerFactory>(),
            NullLogger<PrdbVideoIdentification>.Instance);
    }

    private sealed class FakeSocket(Func<HttpRequestMessage, string, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return answer(request, body);
        }
    }
}
