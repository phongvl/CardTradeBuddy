using CardTradeBuddy.Pages.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace CardTradeBuddy.Pages;

/// <summary>The front page: the login box. Someone who is already logged in goes straight on to the card scanner.</summary>
public class IndexModel(IStringLocalizer<SharedResource> localizer) : PageModel
{
    public LoginForm Form { get; private set; } = new();

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return Redirect(LoginForm.HomePath);

        Form = LoginForm.Fresh(Request, TempData, key => localizer[key]);
        return Page();
    }
}
