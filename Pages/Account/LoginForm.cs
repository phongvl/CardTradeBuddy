using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace CardTradeBuddy.Pages.Account;

/// <summary>What the login box shows. Used by the front page and by /Account/Login (which shows the same box with an error).</summary>
public class LoginForm
{
    /// <summary>Where a successful login goes when nothing else was asked for.</summary>
    public const string HomePath = "/ProtoUpload";

    public string Email { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? Error { get; set; }
    public string? Notice { get; set; }
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// A fresh login box: prefilled when the visitor ticked "Remember me" last time, and showing a one-time notice left by another
    /// page (for example "Your password has been changed"). <paramref name="translate"/> turns the notice's resource key into text.
    /// </summary>
    public static LoginForm Fresh(HttpRequest request, ITempDataDictionary tempData, Func<string, string> translate)
    {
        var form = new LoginForm();
        if (RememberedEmailCookie.Get(request) is { } email)
        {
            form.Email = email;
            form.RememberMe = true;
        }

        // TempData is for one request only: a page sets "Notice" (a resource key) and redirects here.
        if (tempData["Notice"] is string key)
            form.Notice = translate(key);
        if (tempData[VerifiedEmailKey] is string confirmed)
            form.Email = confirmed;

        return form;
    }

    /// <summary>TempData key: the address a visitor just confirmed, so the login box can be prefilled with it.</summary>
    public const string VerifiedEmailKey = "ConfirmedEmail";
}
