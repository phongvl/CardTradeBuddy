using CardTradeBuddy.Data;

namespace CardTradeBuddy.Services;

/// <summary>Saves what a new user told us and accepted at sign-up (birthday, the disclaimer). Best effort like <see cref="UserPreferences"/>: the account already exists in the LoginServer by then.</summary>
public class UserProfiles(AppDbContext db, ILogger<UserProfiles> log)
{
    public async Task SaveSignUpAsync(string userId, DateOnly birthDate, CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            db.UserProfiles.Add(new UserProfile
            {
                UserId = userId,
                BirthDate = birthDate,
                DisclaimerAcceptedAt = now,
                DisclaimerVersion = Disclaimer.Version,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The user exists but their sign-up details were not saved: this needs a look (database down?), so it is an error, not a warning.
            log.LogError(ex, "Could not save the birthday and the accepted disclaimer of the new user {UserId}.", userId);
        }
    }
}
