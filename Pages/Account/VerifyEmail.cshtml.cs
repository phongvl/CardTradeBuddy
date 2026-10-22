using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace CardTradeBuddy.Pages.Account;

/// <summary>
/// The page the confirmation email links to: /Account/VerifyEmail?token=...
/// Opening the link only SHOWS a button; the address is confirmed when the button is pressed (a POST). Email programs and virus
/// scanners open links automatically, and that must not be able to use up the link.
/// </summary>
[EnableRateLimiting("auth")]
public class VerifyEmailModel(LoginServerClient loginServer, FormChecks checks, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }

    public string? Error { get; private set; }

    /// <summary>True when there is nothing to confirm (no token, or the link was found to be invalid).</summary>
    public bool TokenProblem { get; private set; }

    public void OnGet()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            TokenProblem = true;
            Error = localizer["VerifyInvalid"];
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            TokenProblem = true;
            Error = localizer["VerifyInvalid"];
            return Page();
        }

        if (await checks.CheckAsync(HttpContext, timing: false) is { } formProblem)
        {
            Error = localizer[formProblem];
            return Page();
        }

        var outcome = await loginServer.VerifyEmailAsync(Token, HttpContext.RequestAborted);
        switch (outcome.Result)
        {
            case VerifyResult.Ok:
                // Handed to the front page once through TempData (not in the address: an email address does not belong in a URL).
                TempData["Notice"] = "VerifyDone";
                if (outcome.Email is not null) TempData[LoginForm.VerifiedEmailKey] = outcome.Email;
                return Redirect("/");
            case VerifyResult.InvalidToken:
                TokenProblem = true;
                Error = localizer["VerifyInvalid"];
                return Page();
            default:
                Error = localizer["ServiceUnavailable"];
                return Page();
        }
    }
}
