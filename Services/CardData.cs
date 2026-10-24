namespace CardTradeBuddy.Services;

public record CardData
{
    public required string Id { get; init; }
    public required string LocalId { get; init; }
    public required string Name { get; init; }

    /// <summary>The language the card was looked up in, upper case as printed on cards ("EN", "DE", "JA", ...). Needed to find it again later.</summary>
    public string Language { get; init; } = "EN";

    public string? Category { get; init; }
    public string? Rarity { get; init; }
    public string? Illustrator { get; init; }
    public string? Description { get; init; }

    public string? SetId { get; init; }
    public string? SetName { get; init; }
    public int? SetOfficialCardCount { get; init; }

    public string? ImageUrl { get; init; }
    public string? SetLogoUrl { get; init; }

    public int? Hp { get; init; }
    public string? Stage { get; init; }
    public IReadOnlyList<string> Types { get; init; } = [];
    public int? Retreat { get; init; }

    public CardPricing? Pricing { get; init; }

    /// <summary>How the set was confirmed against the printed set code and total, or a warning if they disagree with this card.</summary>
    public string? MatchNote { get; init; }
}

/// <summary>Prices in the currency of each marketplace (Cardmarket: EUR, TCGplayer: USD).</summary>
public record CardPricing
{
    public CardmarketPricing? Cardmarket { get; init; }
    public TcgPlayerPricing? TcgPlayer { get; init; }
}

public record CardmarketPricing
{
    public string? Currency { get; init; }
    public DateTimeOffset? Updated { get; init; }
    public decimal? Average { get; init; }
    public decimal? Low { get; init; }
    public decimal? Trend { get; init; }
    public decimal? Avg1Day { get; init; }
    public decimal? Avg7Days { get; init; }
    public decimal? Avg30Days { get; init; }
    public decimal? HoloAverage { get; init; }
    public decimal? HoloLow { get; init; }
    public decimal? HoloTrend { get; init; }
    public decimal? Avg1DayHolo { get; init; }
    public decimal? Avg7DaysHolo { get; init; }
    public decimal? Avg30DaysHolo { get; init; }
}

public record TcgPlayerPricing
{
    public string? Currency { get; init; }
    public DateTimeOffset? Updated { get; init; }
    public TcgPlayerPrices? Normal { get; init; }
    public TcgPlayerPrices? Holofoil { get; init; }
    public TcgPlayerPrices? ReverseHolofoil { get; init; }
}

public record TcgPlayerPrices
{
    public decimal? Low { get; init; }
    public decimal? Mid { get; init; }
    public decimal? High { get; init; }
    public decimal? Market { get; init; }
    public decimal? DirectLow { get; init; }
}
