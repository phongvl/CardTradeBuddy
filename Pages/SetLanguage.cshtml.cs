using CardTradeBuddy.Localization;
using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardTradeBuddy.Pages;

/// <summary>
/// The language flags post here from every page. Stores the chosen language in the culture cookie (which UseRequestLocalization
/// reads), also as the logged-in user's preferred language, and goes back to the page the visitor came from.
/// </summary>
public class SetLanguageModel(UserPreferences preferences) : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync(string culture, string? returnUrl)
    {
        if (AppLanguages.Codes.Contains(culture))
        {
            LanguageCookie.Set(HttpContext, culture);

            // A logged-in user gets this language back at the next login, on any device.
            if (UserPreferences.UserId(User) is { } userId)
                await preferences.SetLanguageAsync(userId, culture, HttpContext.RequestAborted);
        }

        // Only this site's own addresses, so a crafted link cannot send the visitor somewhere else.
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
