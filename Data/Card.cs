using CardTradeBuddy.Services;

namespace CardTradeBuddy.Data;

/// <summary>
/// A card a user has put in an album (table <c>cards</c>). A card belongs to exactly one album. It keeps everything the card list
/// (TCGdex) said about the card when it was added, including the image addresses, and when its price was last checked, so an album
/// can be shown without asking TCGdex again. The user's own photo is never stored.
/// Owning two copies of a card means two rows.
/// </summary>
public class Card
{
    public Guid Id { get; set; }

    /// <summary>The one album this card is in.</summary>
    public Guid AlbumId { get; set; }

    public Album Album { get; set; } = null!;

    public DateTime AddedAt { get; set; }

    // ---- What the card list said about the card ----

    /// <summary>The card's id in TCGdex, for example "sv10-001".</summary>
    public string CardId { get; set; } = "";

    /// <summary>The number in the set, for example "001".</summary>
    public string LocalId { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Rarity { get; set; }
    public string? Illustrator { get; set; }
    public string? Description { get; set; }

    public string? SetId { get; set; }
    public string? SetName { get; set; }
    public int? SetOfficialCardCount { get; set; }

    /// <summary>Address of the card's picture (on TCGdex's servers).</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Address of the set's logo (on TCGdex's servers).</summary>
    public string? SetLogoUrl { get; set; }

    public int? Hp { get; set; }
    public string? Stage { get; set; }
    public string[] Types { get; set; } = [];
    public int? Retreat { get; set; }

    // ---- Price ----

    /// <summary>The finish the estimated price is for: Normal, Holofoil, ReverseHolofoil or Unknown (the photo could not tell).</summary>
    public CardFinish Finish { get; set; }

    /// <summary>True when the finish could not be seen and the price is for an assumed one.</summary>
    public bool FinishAssumed { get; set; }

    /// <summary>The estimated price in euros for <see cref="Finish"/>, null when there was none (see <see cref="PriceCalculator"/>).</summary>
    public decimal? EstimatedPriceEur { get; set; }

    /// <summary>Every marketplace figure TCGdex had (Cardmarket in EUR, TCGplayer in USD), as received, stored as JSON.</summary>
    public CardPricing? Pricing { get; set; }

    /// <summary>When the price was last looked up. A later price check should update <see cref="Pricing"/>, <see cref="EstimatedPriceEur"/> and this.</summary>
    public DateTime PricesCheckedAt { get; set; }
}
