using System.Globalization;
using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace CardTradeBuddy.Pages.Account;

/// <summary>"I forgot my password": the LoginServer keeps the reset tokens and sends the email; this page only asks it to.</summary>
[EnableRateLimiting("auth")]
public class ForgotPasswordModel(LoginServerClient loginServer, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";

    public bool Sent { get; private set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Error = await RequestResetAsync();
        Sent = Error is null;
        return Page();
    }

    /// <summary>Returns a visitor-safe error message, or null when the request was accepted.</summary>
    private async Task<string?> RequestResetAsync()
    {
        var email = (Email ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0) return localizer["EmailRequired"];
        if (!EmailAddress.IsValid(email)) return localizer["InvalidEmail"];

        // The answer is the same whether or not the email has an account, so nobody can use this to find out who does.
        var accepted = await loginServer.RequestPasswordResetAsync(email, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, HttpContext.RequestAborted);
        return accepted ? null : localizer["ServiceUnavailable"].Value;
    }
}
