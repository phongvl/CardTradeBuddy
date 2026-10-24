namespace CardTradeBuddy.Services;

/// <summary>
/// The cookie that remembers the visitor accepted the cookies (see <c>Pages/Shared/_CookieConsent.cshtml</c>). Until it is there every
/// page shows the consent dialog on top and nothing behind it can be used. The value is the version of the text accepted: raise
/// <see cref="Version"/> when the cookies change in a way that needs a new acceptance.
/// </summary>
public static class ConsentCookie
{
    public const string Name = "cardtradebuddy.consent";

    public const string Version = "1";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    public static bool Has(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && value == Version;

    public static void Set(HttpContext context) =>
        context.Response.Cookies.Append(Name, Version, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,   // it records the choice itself
        });
}
