using Microsoft.AspNetCore.DataProtection;

namespace CardTradeBuddy.Services;

/// <summary>
/// An extra layer on the public forms (login, create user, forgot and reset password) on top of the anti-forgery token, aimed at
/// bots that fill in every field and post at once. Each form carries a hidden field that people never see (a bot fills it in) and
/// a signed time stamp: a post that comes in under <see cref="MinSeconds"/> after the form was shown, or long after, is refused.
/// </summary>
public class FormGuard(IDataProtectionProvider provider, TimeProvider time)
{
    /// <summary>The hidden trap field (hidden with CSS, so it is empty when a person submits).</summary>
    public const string TrapField = "hp_note";

    public const string StampField = "FormStamp";

    /// <summary>People need more than this to fill in a form; a script does not.</summary>
    public const int MinSeconds = 2;

    /// <summary>A form left open longer than this must be reloaded.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(8);

    private readonly IDataProtector _protector = provider.CreateProtector("CardTradeBuddy.FormGuard.v1");

    public string NewStamp() => _protector.Protect(time.GetUtcNow().UtcTicks.ToString());

    public bool IsValid(HttpRequest request)
    {
        if (!request.HasFormContentType) return false;
        var form = request.Form;
        if (!string.IsNullOrEmpty(form[TrapField])) return false;

        try
        {
            var shown = new DateTimeOffset(long.Parse(_protector.Unprotect(form[StampField].ToString())), TimeSpan.Zero);
            var age = time.GetUtcNow() - shown;
            return age >= TimeSpan.FromSeconds(MinSeconds) && age <= MaxAge;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
