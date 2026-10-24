namespace CardTradeBuddy.Services;

/// <summary>
/// Every cookie the site (and the check that you are a person) can set. The ONE list behind the cookie dialog and the Cookies page.
/// <b>When a cookie is added, removed or changed, change it here</b> (and its description in the resource files, key <c>ConsentCookie...</c>),
/// and raise <see cref="ConsentCookie.Version"/> so everybody is asked again.
/// </summary>
public static class CookieList
{
    /// <param name="Name">The cookie's name as the browser shows it.</param>
    /// <param name="DescriptionKey">Resource key of what it is for and how long it lasts.</param>
    public record Entry(string Name, string DescriptionKey);

    public static readonly IReadOnlyList<Entry> All =
    [
        new("cardtradebuddy.session", "ConsentCookieSession"),
        new("cardtradebuddy.antiforgery", "ConsentCookieAntiforgery"),
        new(".AspNetCore.Culture", "ConsentCookieLanguage"),
        new(RememberedEmailCookie.Name, "ConsentCookieEmail"),
        new(".AspNetCore.Mvc.CookieTempDataProvider", "ConsentCookieTempData"),
        new(ConsentCookie.Name, "ConsentCookieConsent"),
        new("Cloudflare Turnstile", "ConsentCookieTurnstile"),
    ];
}
