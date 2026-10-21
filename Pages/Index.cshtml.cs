using CardTradeBuddy.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardTradeBuddy.Pages;

public class IndexModel : PageModel
{
    public void OnGet()
    {

    }

    // Remembers the chosen language in a cookie (which UseRequestLocalization reads) and reloads the page.
    public IActionResult OnPostLanguage(string culture)
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

        return RedirectToPage();
    }
}
