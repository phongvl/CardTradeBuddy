namespace CardTradeBuddy.Services;

/// <summary>All the checks a public form post goes through besides the anti-forgery token: <see cref="FormGuard"/> and Cloudflare Turnstile.</summary>
public class FormChecks(FormGuard guard, TurnstileVerifier turnstile)
{
    /// <summary>The resource key of the error to show, or null when the post may go on.</summary>
    /// <param name="timing">False for a form that is a single button (a person can press it at once), so the trap and the minimum time do not apply.</param>
    public async Task<string?> CheckAsync(HttpContext context, bool timing = true)
    {
        if (timing && !guard.IsValid(context.Request)) return "FormExpired";
        if (!await turnstile.VerifyAsync(context, context.RequestAborted)) return "TurnstileFailed";
        return null;
    }
}
