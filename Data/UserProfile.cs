namespace CardTradeBuddy.Data;

/// <summary>What this app knows about a user besides the preferences (table <c>user_profiles</c>), given when the user signed up.</summary>
public class UserProfile
{
    /// <summary>The LoginServer user id (the <c>NameIdentifier</c> claim).</summary>
    public string UserId { get; set; } = "";

    /// <summary>The birthday the user gave when creating the account. Used to check the age limit; never shown to anyone.</summary>
    public DateOnly BirthDate { get; set; }

    public DateTime CreatedAt { get; set; }
}
