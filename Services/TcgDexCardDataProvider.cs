using System.Text.Json.Serialization;

namespace CardTradeBuddy.Services;

public class TcgDexCardDataProvider(HttpClient http) : ICardDataProvider
{
    public const string BaseUrl = "https://api.tcgdex.net/v2/";

    // Printed language code -> TCGdex language path segment. Anything else falls back to English,
    // which also keeps unvalidated model output out of the request path.
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EN"] = "en",
        ["DE"] = "de",
        ["FR"] = "fr",
        ["IT"] = "it",
        ["ES"] = "es",
        ["PT"] = "pt",
        ["JA"] = "ja",
        ["JP"] = "ja",
    };

    public async Task<CardData?> GetCardByNameAndIdAsync(string name, string localId, string? setCode = null, string? languageCode = null, int? setTotal = null)
    {
        var lang = languageCode is not null && Languages.TryGetValue(languageCode.Trim(), out var l) ? l : "en";

        // Only the name is sent to the API. Its localId filter is a loose "contains" match and
        // has been seen to return nothing for valid IDs, so the ID and exact name are matched here.
        var url = $"{lang}/cards?name={Uri.EscapeDataString(name)}";
        var candidates = await http.GetFromJsonAsync<List<CardBrief>>(url) ?? [];

        var matches = candidates
            .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
                        LocalIdsEqual(c.LocalId, localId))
            .ToList();

        if (matches.Count == 0)
            return null;

        var (match, note) = await ChooseMatchAsync(matches, setCode, setTotal, lang);

        var card = await GetCardWithFreshestPricingAsync($"{lang}/cards/{Uri.EscapeDataString(match.Id)}");
        return card is null ? null : ToCardData(card) with { MatchNote = note, Language = lang.ToUpperInvariant() };
    }

    // TCGdex sits behind several servers that do not always have the same price snapshot: some answers have
    // "tcgplayer": null while others (newer) have it. Ask again while TCGplayer prices are missing and keep the
    // answer with the newest Cardmarket timestamp, preferring one that has TCGplayer data.
    private const int MaxPriceAttempts = 4;

    private async Task<Card?> GetCardWithFreshestPricingAsync(string url)
    {
        Card? best = null;

        for (var attempt = 0; attempt < MaxPriceAttempts; attempt++)
        {
            var card = await http.GetFromJsonAsync<Card>(url);
            if (card is null)
                return best;

            if (best is null || IsFresher(card, best))
                best = card;

            if (best.Pricing?.TcgPlayer is not null)
                break;
        }

        return best;
    }

    private static bool IsFresher(Card candidate, Card current)
    {
        var candidateTime = candidate.Pricing?.Cardmarket?.Updated;
        var currentTime = current.Pricing?.Cardmarket?.Updated;

        if (candidateTime != currentTime)
            return candidateTime > currentTime;

        return candidate.Pricing?.TcgPlayer is not null && current.Pricing?.TcgPlayer is null;
    }

    // A card id is "{setId}-{localId}"; the printed set code ("DRI") and the set's card total (the 182 in
    // 001/182) both live on the set, so each candidate's set is compared with what was read from the card.
    // The total is weighted above the code: digits are read more reliably than the small letters.
    private async Task<(CardBrief Match, string? Note)> ChooseMatchAsync(List<CardBrief> matches, string? setCode, int? setTotal, string lang)
    {
        setCode = string.IsNullOrWhiteSpace(setCode) ? null : setCode.Trim();
        if (setCode is null && setTotal is null)
            return (matches[0], null);

        var sets = await Task.WhenAll(matches.Select(m => GetSetAsync(m, lang)));

        var scored = matches.Select((m, i) =>
        {
            var abbreviation = sets[i]?.Abbreviation?.Official;
            var official = sets[i]?.CardCount?.Official;
            bool? codeOk = setCode is not null && abbreviation is not null
                ? string.Equals(abbreviation, setCode, StringComparison.OrdinalIgnoreCase) : null;
            bool? totalOk = setTotal is not null && official is not null ? official == setTotal : null;
            return (Match: m, Abbreviation: abbreviation, Official: official, CodeOk: codeOk, TotalOk: totalOk,
                    Rank: (totalOk == true ? 2 : 0) + (codeOk == true ? 1 : 0));
        }).ToList();

        // OrderBy is stable, so with no evidence either way the first candidate stays first.
        var best = scored.OrderByDescending(s => s.Rank).First();
        return (best.Match, Describe(best.CodeOk, best.TotalOk, setCode, setTotal, best.Abbreviation, best.Official));
    }

    private static string? Describe(bool? codeOk, bool? totalOk, string? setCode, int? setTotal, string? abbreviation, int? official)
    {
        if (codeOk == true && totalOk == true)
            return $"Verified: set code {setCode} and set total {setTotal} both match.";
        if (totalOk == true && codeOk == false)
            return $"Picked by set total {setTotal}; the read set code \"{setCode}\" does not match {abbreviation} and was ignored.";
        if (totalOk == true)
            return $"Matched on set total {setTotal}.";
        if (codeOk == true && totalOk == false)
            return $"Picked by set code {setCode}, but the read total {setTotal} differs from the set's {official}.";
        if (codeOk == true)
            return $"Matched on set code {setCode}.";
        if (codeOk == false || totalOk == false)
            return $"Warning: the read set code \"{setCode}\" / total {setTotal} do not match this card's set ({abbreviation}, {official} cards); check the name and number.";
        return null;
    }

    private async Task<SetDetail?> GetSetAsync(CardBrief card, string lang)
    {
        var dash = card.Id.LastIndexOf('-');
        return dash <= 0 ? null : await http.GetFromJsonAsync<SetDetail>($"{lang}/sets/{Uri.EscapeDataString(card.Id[..dash])}");
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
                Avg1DayHolo = cm.Avg1Holo,
                Avg7DaysHolo = cm.Avg7Holo,
                Avg30DaysHolo = cm.Avg30Holo,
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

    private record SetDetail(string Id, SetAbbreviation? Abbreviation, SetCardCount? CardCount);

    private record SetAbbreviation(string? Official);

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
        [property: JsonPropertyName("trend-holo")] decimal? TrendHolo,
        [property: JsonPropertyName("avg1-holo")] decimal? Avg1Holo,
        [property: JsonPropertyName("avg7-holo")] decimal? Avg7Holo,
        [property: JsonPropertyName("avg30-holo")] decimal? Avg30Holo);

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
