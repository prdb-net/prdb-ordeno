using System.Net;
using System.Net.Http.Headers;

namespace Prdb.Ordeno.Infrastructure.Tests.Library;

/// <summary>
/// Whatever answers when the tool asks for an image: the socket, and nothing
/// above it.
/// </summary>
/// <remarks>
/// It is deliberately capable of answering badly. Half the reason
/// <see cref="Prdb.Ordeno.Infrastructure.Library.SceneArtwork"/> exists is that
/// the URL is one the tool did not compose and the response is not one it
/// controls — so a test needs a server that can send HTML, a truncated picture,
/// or more bytes than it said it would.
/// </remarks>
internal sealed class FakeCdn : HttpMessageHandler
{
    /// <summary>What a request gets, or <c>null</c> to answer with <see cref="Fails"/>.</summary>
    public byte[]? Image { get; set; } = Jpeg();

    public string ContentType { get; set; } = "image/jpeg";

    /// <summary>What to answer when there is no image to hand back.</summary>
    public HttpStatusCode Fails { get; set; } = HttpStatusCode.NotFound;

    /// <summary>Nothing is listening: no DNS, no route, a CDN that went away.</summary>
    public bool Down { get; set; }

    /// <summary>Every URL the tool asked for, in order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>
    /// A complete, tiny JPEG: the start-of-image marker, some filling, and the
    /// end-of-image marker. Not a picture anything would decode, and it does not
    /// have to be — what is under test is what the tool keeps and where it puts
    /// it, not what a decoder makes of it.
    /// </summary>
    public static byte[] Jpeg(int filling = 64) =>
        [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[filling], 0xFF, 0xD9];

    /// <summary>The same, with the end cut off: a download that stopped halfway.</summary>
    public static byte[] TruncatedJpeg() => [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[64]];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());

        if (Down)
        {
            throw new HttpRequestException("Name or service not known.");
        }

        if (Image is not { } image)
        {
            return Task.FromResult(new HttpResponseMessage(Fails));
        }

        var answer = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(image),
        };

        answer.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);

        return Task.FromResult(answer);
    }
}
