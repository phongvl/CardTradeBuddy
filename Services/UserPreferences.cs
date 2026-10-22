using System.Security.Claims;
using CardTradeBuddy.Data;
using CardTradeBuddy.Localization;
using Microsoft.EntityFrameworkCore;

namespace CardTradeBuddy.Services;

/// <summary>
/// Reads and saves a logged-in user's preferences. Best effort: when the database is down or not set up, the site keeps working
/// (the language still follows the cookie) and the problem is logged, so a preference can never stop a login or a page.
/// </summary>
public class UserPreferences(AppDbContext db, ILogger<UserPreferences> log)
{
    /// <summary>The LoginServer user id of the logged-in user, or null for a visitor.</summary>
    public static string? UserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    /// <summary>The user's saved language, or null if none is saved (or it is no longer a supported language).</summary>
    public async Task<string?> GetLanguageAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var language = await db.UserPreferences.AsNoTracking()
                .Where(p => p.UserId == userId).Select(p => p.Language).SingleOrDefaultAsync(ct);
            return language is not null && AppLanguages.Codes.Contains(language) ? language : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Could not read the saved language of user {UserId}.", userId);
            return null;
        }
    }

    public async Task SetLanguageAsync(string userId, string language, CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var row = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == userId, ct);
            if (row is null) db.UserPreferences.Add(new UserPreference { UserId = userId, Language = language, UpdatedAt = now });
            else { row.Language = language; row.UpdatedAt = now; }
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Two requests inserting the same new user at once: the other one won, the language is saved either way.
            log.LogWarning(ex, "Could not save the language of user {UserId}.", userId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Could not save the language of user {UserId}.", userId);
        }
    }
}
