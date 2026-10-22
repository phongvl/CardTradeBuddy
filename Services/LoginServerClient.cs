using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CardTradeBuddy.Services;

/// <summary>Settings from the "Login" section: the LoginServer's address and this app's registration there.</summary>
public class LoginOptions
{
    public const string Section = "Login";

    /// <summary>The LoginServer's address, e.g. https://secure.phong.dk</summary>
    public string Authority { get; set; } = "";

    /// <summary>The client id this app is registered under in the LoginServer's admin pages.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Secret, never in the repo: user-secrets in development, appsettings.Production.json on the server.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// True: a new user must click a link in an email before they can log in. Needs the app's email sender to be set up in the
    /// LoginServer (otherwise creating users fails). False: the user can log in right away.
    /// </summary>
    public bool VerifyEmail { get; set; }
}

public record SignedInUser(string Id, string Email, bool IsAdmin, bool IsSuperAdmin = false);

public enum LoginFailure { None, InvalidCredentials, LockedOut, NoAccess, NotVerified, Unavailable }

/// <summary><see cref="Detail"/> says why the login service was unavailable. It is for logs and the Development environment only, never for visitors.</summary>
public record LoginResult(SignedInUser? User, LoginFailure Failure, string? Detail = null);

public enum ResetResult { Ok, InvalidToken, WeakPassword, Unavailable }

public enum SignUpOutcome { Created, AlreadyExists, WeakPassword, InvalidEmail, Unavailable }

/// <summary><see cref="UserId"/> is set when the LoginServer made (or re-armed) the account.</summary>
public record SignUpResult(SignUpOutcome Outcome, Guid? UserId = null);

public enum VerifyResult { Ok, InvalidToken, Unavailable }

/// <summary><see cref="Email"/> is the address that was just confirmed (null if the LoginServer did not say).</summary>
public record VerifyOutcome(VerifyResult Result, string? Email = null);

/// <summary>
/// Talks to the LoginServer from this app's SERVER (never from the browser): password login (this app has its own login page),
/// creating users, confirming email addresses and "forgot password". The LoginServer keeps the users and the reset tokens and
/// sends the emails.
/// </summary>
public class LoginServerClient(HttpClient http, IOptions<LoginOptions> options, ILogger<LoginServerClient> log)
{
    // Pages of this app that the emails link to. The LoginServer builds the link from the app's registered address plus this path.
    private const string ResetPath = "/Account/ResetPassword";
    private const string VerifyPath = "/Account/VerifyEmail";

    private LoginOptions Settings => options.Value;

    /// <summary>Which LoginServer this client talks to, for the development detail message and the logs.</summary>
    private string Server => http.BaseAddress?.GetLeftPart(UriPartial.Authority) ?? "(Login:Authority is not set)";

    // ---- Login ----

    public async Task<LoginResult> PasswordLoginAsync(string email, string password, CancellationToken ct = default)
    {
        var result = await RequestPasswordLoginAsync(email, password, ct);

        // Every outcome is logged with the server that answered (never the password), so "why can't I log in" can be answered from the log.
        if (result.User is null)
            log.LogInformation("Login for {Email} was refused by {Server}: {Failure}", email, Server, result.Failure);
        else
            log.LogInformation("Login for {Email} was accepted by {Server}", email, Server);
        return result;
    }

    private async Task<LoginResult> RequestPasswordLoginAsync(string email, string password, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsync("connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = email,
                ["password"] = password,
                ["client_id"] = Settings.ClientId,
                ["client_secret"] = Settings.ClientSecret,
                ["scope"] = "openid email profile",
            }), ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var user = ReadUser(body);
                if (user is not null) return new LoginResult(user, LoginFailure.None);
                log.LogError("The LoginServer at {Server} returned a token this app could not read.", Server);
                return new LoginResult(null, LoginFailure.Unavailable, $"The LoginServer at {Server} returned a token this app could not read.");
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
            {
                var description = ReadString(body, "error_description") ?? "";
                var error = ReadString(body, "error");
                if (error is "invalid_client" or "unauthorized_client" or "invalid_scope" or "unsupported_grant_type")
                {
                    log.LogError("The LoginServer at {Server} rejected this app ({Error}: {Description}). Check Login:Authority, Login:ClientId and Login:ClientSecret.", Server, error, description);
                    return new LoginResult(null, LoginFailure.Unavailable, $"The LoginServer at {Server} rejected this app ({error}: {description}). Check Login:Authority, Login:ClientId and Login:ClientSecret.");
                }

                // The LoginServer answers with a safe message for each case.
                if (description.Contains("Too many", StringComparison.OrdinalIgnoreCase)) return new LoginResult(null, LoginFailure.LockedOut);
                if (description.Contains("not confirmed", StringComparison.OrdinalIgnoreCase)) return new LoginResult(null, LoginFailure.NotVerified);
                if (description.Contains("access to this application", StringComparison.OrdinalIgnoreCase)) return new LoginResult(null, LoginFailure.NoAccess);
                return new LoginResult(null, LoginFailure.InvalidCredentials);
            }

            var reason = $"The LoginServer at {Server} answered {(int)response.StatusCode} ({ReadString(body, "error") ?? "no error code"}: {ReadString(body, "error_description") ?? "no details"}).";
            log.LogError("{Reason} (to a login)", reason);
            return new LoginResult(null, LoginFailure.Unavailable, reason);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            log.LogError(ex, "Could not reach the LoginServer at {Server} for a login.", Server);
            return new LoginResult(null, LoginFailure.Unavailable, $"Could not reach the LoginServer at {Server} ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    // ---- Create user and confirm the email address ----

    /// <summary>
    /// Creates the account in the LoginServer. With <see cref="LoginOptions.VerifyEmail"/> the LoginServer sends a confirmation email
    /// and keeps the user switched off until the link in it is used. <see cref="SignUpOutcome.AlreadyExists"/> must be shown to the
    /// visitor exactly like <see cref="SignUpOutcome.Created"/>, so the form cannot be used to find out who has an account.
    /// </summary>
    public async Task<SignUpResult> CreateUserAsync(string email, string password, string culture, CancellationToken ct = default)
    {
        try
        {
            var bearer = await GetServiceTokenAsync("users.create", ct);
            if (bearer is null) return new SignUpResult(SignUpOutcome.Unavailable);

            object payload = Settings.VerifyEmail
                ? new { email, password, verifyEmail = true, culture, verifyPath = VerifyPath }
                : new { email, password };
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/users") { Content = JsonContent.Create(payload) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == HttpStatusCode.Created)
                return new SignUpResult(SignUpOutcome.Created, Guid.TryParse(ReadString(body, "id"), out var id) ? id : null);
            if (response.StatusCode == HttpStatusCode.Conflict) return new SignUpResult(SignUpOutcome.AlreadyExists);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                switch (ReadString(body, "error"))
                {
                    case "weak_password": return new SignUpResult(SignUpOutcome.WeakPassword);
                    case "invalid_email": return new SignUpResult(SignUpOutcome.InvalidEmail);
                }
            }

            log.LogError("The LoginServer answered {Status} ({Error}: {Description}) to a sign-up.", (int)response.StatusCode, ReadString(body, "error"), ReadString(body, "error_description"));
            return new SignUpResult(SignUpOutcome.Unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            log.LogError(ex, "Could not reach the LoginServer for a sign-up.");
            return new SignUpResult(SignUpOutcome.Unavailable);
        }
    }

    /// <summary>Confirms the email address behind the link in the confirmation email; the account can log in afterwards.</summary>
    public async Task<VerifyOutcome> VerifyEmailAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var bearer = await GetServiceTokenAsync("users.verify", ct);
            if (bearer is null) return new VerifyOutcome(VerifyResult.Unavailable);

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/users/verify-email") { Content = JsonContent.Create(new { token }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.OK)
                return new VerifyOutcome(VerifyResult.Ok, ReadString(await response.Content.ReadAsStringAsync(ct), "email"));
            if (response.StatusCode == HttpStatusCode.NoContent) return new VerifyOutcome(VerifyResult.Ok);
            if (response.StatusCode == HttpStatusCode.BadRequest) return new VerifyOutcome(VerifyResult.InvalidToken);

            log.LogError("The LoginServer answered {Status} to an email confirmation.", (int)response.StatusCode);
            return new VerifyOutcome(VerifyResult.Unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            log.LogError(ex, "Could not reach the LoginServer for an email confirmation.");
            return new VerifyOutcome(VerifyResult.Unavailable);
        }
    }

    // ---- Forgot password ----

    /// <summary>
    /// Asks the LoginServer to email a reset link. True means the request was accepted (this says nothing about whether the
    /// email has an account; the LoginServer never tells). False means the LoginServer could not be used.
    /// </summary>
    public async Task<bool> RequestPasswordResetAsync(string email, string culture, CancellationToken ct = default)
    {
        try
        {
            var token = await GetServiceTokenAsync("passwords.reset", ct);
            if (token is null) return false;

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/password-reset/request")
            {
                Content = JsonContent.Create(new { email, culture, resetPath = ResetPath }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            // 400 = the LoginServer found the email malformed; the page already checked, so treat it like "accepted".
            if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.BadRequest) return true;

            log.LogError("The LoginServer answered {Status} to a password reset request.", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            log.LogError(ex, "Could not reach the LoginServer for a password reset request.");
            return false;
        }
    }

    public async Task<ResetResult> ConfirmPasswordResetAsync(string token, string newPassword, CancellationToken ct = default)
    {
        try
        {
            var bearer = await GetServiceTokenAsync("passwords.reset", ct);
            if (bearer is null) return ResetResult.Unavailable;

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/password-reset/confirm")
            {
                Content = JsonContent.Create(new { token, newPassword }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NoContent) return ResetResult.Ok;
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var error = ReadString(await response.Content.ReadAsStringAsync(ct), "error");
                return error == "weak_password" ? ResetResult.WeakPassword : ResetResult.InvalidToken;
            }

            log.LogError("The LoginServer answered {Status} to a password reset.", (int)response.StatusCode);
            return ResetResult.Unavailable;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            log.LogError(ex, "Could not reach the LoginServer for a password reset.");
            return ResetResult.Unavailable;
        }
    }

    // ---- Helpers ----

    /// <summary>A token for this app itself (client credentials) with ONE scope: "passwords.reset", "users.create" or "users.verify".</summary>
    private async Task<string?> GetServiceTokenAsync(string scope, CancellationToken ct)
    {
        using var response = await http.PostAsync("connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Settings.ClientId,
            ["client_secret"] = Settings.ClientSecret,
            ["scope"] = scope,
        }), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode) return ReadString(body, "access_token");

        log.LogError("The LoginServer refused a service token ({Status}: {Error}). Check Login:ClientId / Login:ClientSecret and that the app has the setting for scope '{Scope}' switched on in the LoginServer.",
            (int)response.StatusCode, ReadString(body, "error_description"), scope);
        return null;
    }

    /// <summary>
    /// Reads who logged in from the access token. The token comes straight from the LoginServer over HTTPS in answer to our own
    /// request (authenticated with the client secret), so it is not re-validated here.
    /// </summary>
    private SignedInUser? ReadUser(string tokenResponse)
    {
        var accessToken = ReadString(tokenResponse, "access_token");
        var parts = accessToken?.Split('.');
        if (parts is not { Length: 3 }) return null;
        try
        {
            using var claims = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = claims.RootElement;
            var id = root.GetProperty("sub").GetString();
            var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
            var app = root.TryGetProperty("app", out var a) ? a.GetString() : null;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(email) || app != Settings.ClientId) return null;
            var isAdmin = root.TryGetProperty("is_admin", out var admin) && admin.GetString() == "true";
            var isSuperAdmin = root.TryGetProperty("is_super_admin", out var sa) && sa.GetString() == "true";
            return new SignedInUser(id, email, isAdmin, isSuperAdmin);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    private static string? ReadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
