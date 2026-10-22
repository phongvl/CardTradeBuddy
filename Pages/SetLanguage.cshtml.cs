using CardTradeBuddy.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardTradeBuddy.Pages;

/// <summary>
/// The language flags post here from every page. Stores the chosen language in the culture cookie (which UseRequestLocalization
/// reads) and goes back to the page the visitor came from.
/// </summary>
public class SetLanguageModel : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public IActionResult OnPost(string culture, string? returnUrl)
    {
        if (AppLanguages.Codes.Contains(culture))
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps,
                });
        }

        // Only this site's own addresses, so a crafted link cannot send the visitor somewhere else.
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
