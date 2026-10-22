using System.Globalization;
using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace CardTradeBuddy.Pages.Account;

/// <summary>"Create user": this app's server asks the LoginServer to create the account. The LoginServer decides what the new user may do.</summary>
[EnableRateLimiting("auth")]
public class RegisterModel(LoginServerClient loginServer, IOptions<LoginOptions> options, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public string? Error { get; private set; }

    /// <summary>True when the LoginServer accepted the sign-up (shown the same whether the address was new or already known).</summary>
    public bool Done { get; private set; }

    public string? DoneMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Error = await SignUpAsync();
        if (Error is null)
        {
            Done = true;
            DoneMessage = localizer[options.Value.VerifyEmail ? "RegisterDoneVerify" : "RegisterDone"];
        }

        return Page();
    }

    /// <summary>Returns a visitor-safe error message, or null when the sign-up was accepted.</summary>
    private async Task<string?> SignUpAsync()
    {
        var email = (Email ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0) return localizer["EmailRequired"];
        if (!EmailAddress.IsValid(email)) return localizer["InvalidEmail"];
        if (string.IsNullOrEmpty(Password)) return localizer["PasswordRequired"];
        if (Password.Length < PasswordRules.MinLength) return localizer["PasswordTooShort", PasswordRules.MinLength];
        if (Password != ConfirmPassword) return localizer["PasswordsDiffer"];

        var result = await loginServer.CreateUserAsync(email, Password, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, HttpContext.RequestAborted);
        return result.Outcome switch
        {
            SignUpOutcome.WeakPassword => localizer["PasswordTooShort", PasswordRules.MinLength].Value,
            SignUpOutcome.InvalidEmail => localizer["InvalidEmail"].Value,
            SignUpOutcome.Unavailable => localizer["ServiceUnavailable"].Value,
            // Created or "already exists": the visitor sees the same answer, so nobody can find out who has an account.
            _ => null,
        };
    }
}
