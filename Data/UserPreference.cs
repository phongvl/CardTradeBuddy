namespace CardTradeBuddy.Data;

/// <summary>
/// One row per user with this app's own settings for that user (table <c>user_preferences</c>). New settings are new columns.
/// The user is identified by the LoginServer user id; this app has no user table of its own.
/// </summary>
public class UserPreference
{
    /// <summary>The LoginServer user id (the <c>NameIdentifier</c> claim).</summary>
    public string UserId { get; set; } = "";

    /// <summary>The language the user last picked ("en", "da"), or null if they never picked one.</summary>
    public string? Language { get; set; }

    public DateTime UpdatedAt { get; set; }
}
