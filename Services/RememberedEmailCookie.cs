namespace CardTradeBuddy.Services;

/// <summary>
/// "Remember me" also remembers the email address, so the login form is prefilled next time (also after logging out).
/// Only the email is kept, never the password. The cookie is set when the box is ticked at login and deleted when it is not.
/// </summary>
public static class RememberedEmailCookie
{
    public const string Name = "cardtradebuddy.email";

    public static string? Get(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var email) && email.Length is > 0 and <= 256 ? email : null;

    public static void Set(HttpContext context, string email, int days) =>
        context.Response.Cookies.Append(Name, email, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(days),
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,   // the user asked for it by ticking the box
        });

    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(Name, new CookieOptions { Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax });
}
