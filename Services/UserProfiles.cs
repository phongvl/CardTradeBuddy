using CardTradeBuddy.Data;

namespace CardTradeBuddy.Services;

/// <summary>Saves what a new user told us at sign-up. Best effort like <see cref="UserPreferences"/>: the account already exists in the LoginServer by then.</summary>
public class UserProfiles(AppDbContext db, ILogger<UserProfiles> log)
{
    public async Task SaveBirthDateAsync(string userId, DateOnly birthDate, CancellationToken ct = default)
    {
        try
        {
            db.UserProfiles.Add(new UserProfile { UserId = userId, BirthDate = birthDate, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The user exists but their birthday was not saved: this needs a look (database down?), so it is an error, not a warning.
            log.LogError(ex, "Could not save the birthday of the new user {UserId}.", userId);
        }
    }
}
