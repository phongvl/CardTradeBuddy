using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace CardTradeBuddy.Pages.Account;

/// <summary>The page the "forgot password" email links to: /Account/ResetPassword?token=...</summary>
[EnableRateLimiting("auth")]
public class ResetPasswordModel(LoginServerClient loginServer, FormChecks checks, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public string? Error { get; private set; }

    /// <summary>True when the form cannot work: no token, or the link was found to be invalid or expired.</summary>
    public bool TokenProblem { get; private set; }

    public void OnGet()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            TokenProblem = true;
            Error = localizer["ResetInvalid"];
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            TokenProblem = true;
            Error = localizer["ResetInvalid"];
            return Page();
        }
        if (await checks.CheckAsync(HttpContext) is { } formProblem)
        {
            Error = localizer[formProblem];
            return Page();
        }
        if (NewPassword.Length < PasswordRules.MinLength)
        {
            Error = localizer["PasswordTooShort", PasswordRules.MinLength];
            return Page();
        }
        if (NewPassword != ConfirmPassword)
        {
            Error = localizer["PasswordsDiffer"];
            return Page();
        }

        switch (await loginServer.ConfirmPasswordResetAsync(Token, NewPassword, HttpContext.RequestAborted))
        {
            case ResetResult.Ok:
                TempData["Notice"] = "ResetDone";
                return Redirect("/");
            case ResetResult.WeakPassword:
                Error = localizer["PasswordTooShort", PasswordRules.MinLength];
                return Page();
            case ResetResult.InvalidToken:
                TokenProblem = true;
                Error = localizer["ResetInvalid"];
                return Page();
            default:
                Error = localizer["ServiceUnavailable"];
                return Page();
        }
    }
}
