using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardTradeBuddy.Pages;

/// <summary>The cookie policy: which cookies the site uses and why. Open to everybody, also before the cookie dialog has been accepted.</summary>
public class CookiesModel : PageModel
{
    /// <summary>When the page was last changed in substance (shown on the page). Change it together with <see cref="Services.ConsentCookie.Version"/> when a cookie changes.</summary>
    public const string Updated = "2026-10-24";

    public void OnGet()
    {
    }
}
