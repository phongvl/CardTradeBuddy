using System.Security.Claims;
using CardTradeBuddy.Localization;
using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace CardTradeBuddy.Pages.Account;

/// <summary>
/// Logs in with the credentials from the login box. The password goes from this server to the LoginServer, which decides; this
/// page then starts the session (a cookie). Also the page a not-logged-in visitor lands on when opening a protected page.
/// </summary>
[EnableRateLimiting("auth")]
public class LoginModel(LoginServerClient loginServer, UserPreferences preferences, IStringLocalizer<SharedResource> localizer, IWebHostEnvironment environment) : PageModel
{
    /// <summary>"Remember me": the login cookie lasts this many days instead of ending when the browser closes.</summary>
    public const int RememberDays = 30;

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public bool RememberMe { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public LoginForm Form { get; private set; } = new();

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(SafeReturnUrl());

        Form = LoginForm.Fresh(Request, TempData, key => localizer[key]);
        Form.ReturnUrl = ReturnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var error = await SignInAsync();
        if (error is null)
            return LocalRedirect(SafeReturnUrl());

        // The password is never sent back to the page; the email and the tick box are.
        Form = new LoginForm { Email = (Email ?? "").Trim(), RememberMe = RememberMe, Error = error, ReturnUrl = ReturnUrl };
        return Page();
    }

    /// <summary>Checks the credentials with the LoginServer and starts the session. Returns a visitor-safe error message, or null on success.</summary>
    private async Task<string?> SignInAsync()
    {
        var email = (Email ?? "").Trim();
        if (email.Length == 0) return localizer["EmailRequired"];
        if (!EmailAddress.IsValid(email)) return localizer["InvalidEmail"];
        if (string.IsNullOrEmpty(Password)) return localizer["PasswordRequired"];

        var result = await loginServer.PasswordLoginAsync(email, Password, HttpContext.RequestAborted);
        if (result.User is not { } user)
        {
            var message = localizer[result.Failure switch
            {
                LoginFailure.LockedOut => "LoginLocked",
                LoginFailure.NoAccess => "LoginNoAccess",
                LoginFailure.NotVerified => "LoginNotVerified",
                LoginFailure.Unavailable => "ServiceUnavailable",
                _ => "LoginInvalid",
            }].Value;

            // Development only: say why the login service was unavailable (visitors never see this).
            if (environment.IsDevelopment() && result.Failure == LoginFailure.Unavailable && result.Detail is not null)
                message += $" [dev detail: {result.Detail}]";
            return message;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Email),
            new("is_admin", user.IsAdmin ? "true" : "false"),
            new("is_super_admin", user.IsSuperAdmin ? "true" : "false"),
        };
        if (user.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, "admin"));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);

        // Without "remember me" this is a session cookie (gone when the browser closes, 8 hours of inactivity at most). With it the
        // cookie is kept for RememberDays, renewed while the user keeps coming back.
        var properties = new AuthenticationProperties
        {
            IsPersistent = RememberMe,
            ExpiresUtc = RememberMe ? DateTimeOffset.UtcNow.AddDays(RememberDays) : null,
            AllowRefresh = true,
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);

        // The user's preferred language (saved when they last picked one) wins over this browser's cookie, so it follows them to any device.
        if (await preferences.GetLanguageAsync(user.Id, HttpContext.RequestAborted) is { } language)
            LanguageCookie.Set(HttpContext, language);

        if (RememberMe) RememberedEmailCookie.Set(HttpContext, user.Email, RememberDays);
        else RememberedEmailCookie.Clear(HttpContext);
        return null;
    }

    /// <summary>Only this site's own addresses are accepted, so a crafted link cannot send the user to another site after login.</summary>
    private string SafeReturnUrl() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : LoginForm.HomePath;
}
