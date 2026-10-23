using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardTradeBuddy.Pages;

/// <summary>
/// The page a user lands on after login: two big halves, "Check a card" (the card scanner) and "Fair swap?". Only for logged-in
/// users (see the AuthorizePage convention in Program.cs); a visitor is sent to the login page.
/// </summary>
public class StartModel : PageModel
{
    public void OnGet()
    {
    }
}
