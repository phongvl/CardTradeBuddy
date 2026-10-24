namespace CardTradeBuddy.Data;

/// <summary>What this app knows about a user besides the preferences (table <c>user_profiles</c>), given when the user signed up.</summary>
public class UserProfile
{
    /// <summary>The LoginServer user id (the <c>NameIdentifier</c> claim).</summary>
    public string UserId { get; set; } = "";

    /// <summary>The birthday the user gave when creating the account. Used to check the age limit; never shown to anyone.</summary>
    public DateOnly BirthDate { get; set; }

    /// <summary>When the user accepted the disclaimer (prices and information may be inaccurate) at sign-up (UTC). Null for users created before it existed.</summary>
    public DateTime? DisclaimerAcceptedAt { get; set; }

    /// <summary>Which version of the disclaimer text was accepted (<see cref="CardTradeBuddy.Services.Disclaimer.Version"/>).</summary>
    public string? DisclaimerVersion { get; set; }

    public DateTime CreatedAt { get; set; }
}
