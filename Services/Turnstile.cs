using System.Text.Json;

namespace CardTradeBuddy.Services;

/// <summary>Settings from the "Turnstile" section. Without both keys Turnstile is off (the other form protection still applies).</summary>
public class TurnstileOptions
{
    public const string Section = "Turnstile";

    /// <summary>The public key put in the page (data-sitekey).</summary>
    public string SiteKey { get; set; } = "";

    /// <summary>The private key used to check the visitor's answer with Cloudflare. Never in the repo.</summary>
    public string SecretKey { get; set; } = "";
}

/// <summary>
/// Cloudflare Turnstile, the "are you a person" check on the public forms. The widget in the page gets a one-time token from
/// Cloudflare and posts it as <c>cf-turnstile-response</c>; this server then asks Cloudflare whether the token is genuine
/// (https://developers.cloudflare.com/turnstile/get-started/server-side-validation/). A token works once.
/// </summary>
public class TurnstileVerifier(HttpClient http, IConfiguration configuration, ILogger<TurnstileVerifier> log)
{
    public const string ResponseField = "cf-turnstile-response";

    private TurnstileOptions Options => configuration.GetSection(TurnstileOptions.Section).Get<TurnstileOptions>() ?? new();

    public bool Enabled => Options.SiteKey.Length > 0 && Options.SecretKey.Length > 0;

    public string SiteKey => Options.SiteKey;

    /// <summary>True when the visitor passed. False when the token is missing, wrong, used before or expired, or Cloudflare could not be asked.</summary>
    public async Task<bool> VerifyAsync(HttpContext context, CancellationToken ct = default)
    {
        if (!Enabled) return true;

        var token = context.Request.HasFormContentType ? context.Request.Form[ResponseField].ToString() : "";
        if (token.Length is 0 or > 2048) return false;

        try
        {
            var form = new Dictionary<string, string> { ["secret"] = Options.SecretKey, ["response"] = token };
            if (ClientIp.Get(context) is { } ip) form["remoteip"] = ip;

            using var response = await http.PostAsync("turnstile/v0/siteverify", new FormUrlEncodedContent(form), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("success", out var success) && success.GetBoolean()) return true;

            var codes = json.RootElement.TryGetProperty("error-codes", out var list) ? string.Join(", ", list.EnumerateArray().Select(e => e.GetString())) : "";
            log.LogInformation("Turnstile refused a form post ({Codes}).", codes);
            // These mean this app's keys are wrong, not that the visitor is: worth an error in the log.
            if (codes.Contains("invalid-input-secret") || codes.Contains("missing-input-secret"))
                log.LogError("Turnstile:SecretKey is not accepted by Cloudflare, so every form post is refused. Check the keys.");
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            // Cloudflare unreachable: the widget could not have loaded either, so nobody is let through by mistake.
            log.LogError(ex, "Could not check a Turnstile token with Cloudflare.");
            return false;
        }
    }
}
