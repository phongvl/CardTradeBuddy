using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardTradeBuddy.Pages;

/// <summary>Takes the "Accept and continue" post of the cookie dialog: remembers the choice and goes back to the page the visitor was on.</summary>
public class ConsentModel : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public IActionResult OnPost(string? returnUrl)
    {
        ConsentCookie.Set(HttpContext);

        // Only this site's own addresses, so a crafted link cannot send the visitor somewhere else.
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
