using Microsoft.AspNetCore.Localization;

namespace CardTradeBuddy.Localization;

/// <summary>The cookie UseRequestLocalization reads to pick the language of a request.</summary>
public static class LanguageCookie
{
    public static void Set(HttpContext context, string culture) =>
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
            });
}
