namespace CardTradeBuddy.Data;

/// <summary>
/// A user's album of cards (table <c>albums</c>). Every user has one default album, created the first time a card is added.
/// Users cannot create more albums yet; the table is ready for it (a user may own many albums, exactly one of them the default).
/// </summary>
public class Album
{
    public const string DefaultName = "My cards";

    public Guid Id { get; set; }

    /// <summary>The LoginServer user id (the <c>NameIdentifier</c> claim) of the owner.</summary>
    public string UserId { get; set; } = "";

    /// <summary>The album's name. The default album is called <see cref="DefaultName"/> here and shown in the user's language.</summary>
    public string Name { get; set; } = DefaultName;

    /// <summary>True for the album that cards are added to when no album is chosen. Exactly one per user (a unique index enforces it).</summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<Card> Cards { get; set; } = [];
}
