namespace Prdb.Ordeno.Host.Access;

/// <summary>
/// The cookie carrying the opaque session token (ADR 0010).
/// </summary>
internal static class SessionCookie
{
    public const string Name = "ordeno_session";

    public static void Write(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, token, OptionsFor(response, expiresAt));

    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, OptionsFor(response, expiresAt: null));

    private static CookieOptions OptionsFor(HttpResponse response, DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        // Only when the request already arrived over https. Marking it Secure on
        // a plain-http LAN installation would make the browser drop it, and the
        // documentation says plainly what plain http over an untrusted network
        // costs.
        Secure = response.HttpContext.Request.IsHttps,
        Path = "/",
        Expires = expiresAt,
    };
}
