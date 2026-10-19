using System.Text.Json.Serialization;

namespace CardTradeBuddy.Services;

public class TcgDexCardDataProvider(HttpClient http) : ICardDataProvider
{
    public const string BaseUrl = "https://api.tcgdex.net/v2/en/";

    public async Task<CardData?> GetCardByNameAndIdAsync(string name, string localId)
    {
        // Only the name is sent to the API. Its localId filter is a loose "contains" match and
        // has been seen to return nothing for valid IDs, so the ID and exact name are matched here.
        var url = $"cards?name={Uri.EscapeDataString(name)}";
        var candidates = await http.GetFromJsonAsync<List<CardBrief>>(url) ?? [];

        var match = candidates.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
            LocalIdsEqual(c.LocalId, localId));

        if (match is null)
            return null;

        var card = await http.GetFromJsonAsync<Card>($"cards/{Uri.EscapeDataString(match.Id)}");
        return card is null ? null : ToCardData(card);
    }

    // Sets are inconsistent about zero padding ("025" vs "25"), so purely numeric IDs
    // compare by value; anything else ("TG01", "SWSH062") compares as text.
    private static bool LocalIdsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
        (int.TryParse(a, out var x) && int.TryParse(b, out var y) && x == y);

    private static CardData ToCardData(Card c) => new()
    {
        Id = c.Id,
        LocalId = c.LocalId,
        Name = c.Name,
        Category = c.Category,
        Rarity = c.Rarity,
        Illustrator = c.Illustrator,
        Description = c.Description,
        SetId = c.Set?.Id,
        SetName = c.Set?.Name,
        SetOfficialCardCount = c.Set?.CardCount?.Official,
        // The API returns the image without extension; add quality and format.
        ImageUrl = c.Image is null ? null : $"{c.Image}/high.webp",
        SetLogoUrl = c.Set?.Logo is null ? null : $"{c.Set.Logo}.webp",
        Hp = c.Hp,
        Stage = c.Stage,
        Types = c.Types ?? [],
        Retreat = c.Retreat,
        Pricing = c.Pricing is null ? null : new CardPricing
        {
            Cardmarket = c.Pricing.Cardmarket is { } cm ? new CardmarketPricing
            {
                Currency = cm.Unit,
                Updated = cm.Updated,
                Average = cm.Avg,
                Low = cm.Low,
                Trend = cm.Trend,
                Avg1Day = cm.Avg1,
                Avg7Days = cm.Avg7,
                Avg30Days = cm.Avg30,
                HoloAverage = cm.AvgHolo,
                HoloLow = cm.LowHolo,
                HoloTrend = cm.TrendHolo,
            } : null,
            TcgPlayer = c.Pricing.TcgPlayer is { } tp ? new TcgPlayerPricing
            {
                Currency = tp.Unit,
                Updated = tp.Updated,
                Normal = Map(tp.Normal),
                Holofoil = Map(tp.Holofoil),
                ReverseHolofoil = Map(tp.ReverseHolofoil),
            } : null,
        },
    };

    private static TcgPlayerPrices? Map(TcgPlayerPriceDto? p) => p is null ? null : new()
    {
        Low = p.LowPrice,
        Mid = p.MidPrice,
        High = p.HighPrice,
        Market = p.MarketPrice,
        DirectLow = p.DirectLowPrice,
    };

    // ---- TCGdex response DTOs (only the fields we use) ----

    private record CardBrief(string Id, string LocalId, string Name);

    private record Card(
        string Id,
        string LocalId,
        string Name,
        string? Category,
        string? Rarity,
        string? Illustrator,
        string? Description,
        string? Image,
        SetBrief? Set,
        int? Hp,
        string? Stage,
        List<string>? Types,
        int? Retreat,
        PricingDto? Pricing);

    private record SetBrief(string Id, string Name, string? Logo, SetCardCount? CardCount);

    private record SetCardCount(int? Official);

    private record PricingDto(CardmarketDto? Cardmarket, [property: JsonPropertyName("tcgplayer")] TcgPlayerDto? TcgPlayer);

    private record CardmarketDto(
        string? Unit,
        DateTimeOffset? Updated,
        decimal? Avg,
        decimal? Low,
        decimal? Trend,
        decimal? Avg1,
        decimal? Avg7,
        decimal? Avg30,
        [property: JsonPropertyName("avg-holo")] decimal? AvgHolo,
        [property: JsonPropertyName("low-holo")] decimal? LowHolo,
        [property: JsonPropertyName("trend-holo")] decimal? TrendHolo);

    private record TcgPlayerDto(
        string? Unit,
        DateTimeOffset? Updated,
        TcgPlayerPriceDto? Normal,
        TcgPlayerPriceDto? Holofoil,
        [property: JsonPropertyName("reverse-holofoil")] TcgPlayerPriceDto? ReverseHolofoil);

    private record TcgPlayerPriceDto(
        decimal? LowPrice,
        decimal? MidPrice,
        decimal? HighPrice,
        decimal? MarketPrice,
        decimal? DirectLowPrice);
}
