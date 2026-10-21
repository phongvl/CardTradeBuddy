namespace CardTradeBuddy.Services;

public enum CardFinish
{
    Unknown,
    Normal,
    Holofoil,
    ReverseHolofoil,
}

/// <summary>
/// The most likely price of a card for one finish, per marketplace and combined.
/// </summary>
/// <param name="Finish">The finish the figures are for.</param>
/// <param name="FinishAssumed">True when the finish was unknown and had to be assumed (see <see cref="PriceCalculator"/>).</param>
/// <param name="CardmarketEur">Cardmarket's estimate, in EUR.</param>
/// <param name="TcgPlayerUsd">TCGplayer's estimate, in USD.</param>
/// <param name="TcgPlayerEur">TCGplayer's estimate converted to EUR; null when there is no TCGplayer price or no exchange rate.</param>
/// <param name="AverageEur">Mean of the two marketplaces' EUR estimates, or the single one available. Null when the
/// marketplaces disagree too much for the mean to be meaningful (see <paramref name="PricesDisagree"/>).</param>
/// <param name="PricesDisagree">True when both EUR estimates exist but differ by more than the reliability tolerance.</param>
public record CalculatedAveragePrice(
    CardFinish Finish,
    bool FinishAssumed,
    decimal? CardmarketEur,
    decimal? TcgPlayerUsd,
    decimal? TcgPlayerEur,
    decimal? AverageEur,
    bool PricesDisagree = false);

public static class PriceCalculator
{
    // A figure counts as reliable when it is within RelativeTolerance of the reference value, or within
    // AbsoluteTolerance of it. The absolute part keeps a few cents of difference on a cheap card from
    // counting as an outlier (0.02 vs 0.07 is 250% apart but only 5 cents).
    private const decimal RelativeTolerance = 0.5m;
    private const decimal AbsoluteTolerance = 0.10m;

    /// <summary>
    /// Estimates the most likely price from the raw marketplace data, using every figure available and
    /// dropping the ones that are out of line with the rest.
    /// <list type="bullet">
    /// <item>Cardmarket: the trend, the 7- and 30-day averages and the overall average. The plain fields are used
    /// for the normal print and the "-holo" fields for holofoil and reverse holofoil, which Cardmarket does not
    /// tell apart. Falls back to the low if none of those exist.</item>
    /// <item>TCGplayer: the market price (based on recent sales) and the mid listing. The market price is the
    /// reference, so a mid that is far above it is dropped. Falls back to the low if neither exists.</item>
    /// <item>Highest and lowest listings are otherwise never used; they are one seller's price.</item>
    /// <item>Within each marketplace the figures that survive are averaged. The two marketplaces are then averaged
    /// in EUR, unless they disagree beyond the tolerance, in which case no combined figure is given.</item>
    /// </list>
    /// With <see cref="CardFinish.Unknown"/> the normal print is assumed if the card has one, otherwise the
    /// cheapest finish that has data, and <see cref="CalculatedAveragePrice.FinishAssumed"/> is set.
    /// Returns null when there is no usable price at all.
    /// </summary>
    /// <param name="usdToEur">Euros per US dollar; when null, TCGplayer is not converted and <c>AverageEur</c> uses Cardmarket only.</param>
    public static CalculatedAveragePrice? Calculate(CardPricing? pricing, CardFinish finish = CardFinish.Unknown, decimal? usdToEur = null)
    {
        if (pricing is null)
            return null;

        if (finish != CardFinish.Unknown)
            return For(pricing, finish, assumed: false, usdToEur);

        return CalculateAll(pricing, usdToEur)
            .Select(p => p with { FinishAssumed = true })
            .OrderBy(p => p.Finish == CardFinish.Normal ? 0 : 1)
            .ThenBy(p => p.TcgPlayerUsd ?? p.CardmarketEur ?? decimal.MaxValue)
            .FirstOrDefault();
    }

    /// <summary>The estimate for every finish that has price data (normal, holofoil, reverse holofoil).</summary>
    public static IReadOnlyList<CalculatedAveragePrice> CalculateAll(CardPricing? pricing, decimal? usdToEur = null)
    {
        if (pricing is null)
            return [];

        return new[] { CardFinish.Normal, CardFinish.Holofoil, CardFinish.ReverseHolofoil }
            .Select(f => For(pricing, f, assumed: false, usdToEur))
            .OfType<CalculatedAveragePrice>()
            .ToList();
    }

    private static CalculatedAveragePrice? For(CardPricing pricing, CardFinish finish, bool assumed, decimal? usdToEur)
    {
        var tcgPrices = finish switch
        {
            CardFinish.Normal => pricing.TcgPlayer?.Normal,
            CardFinish.Holofoil => pricing.TcgPlayer?.Holofoil,
            _ => pricing.TcgPlayer?.ReverseHolofoil,
        };

        var tcg = TcgPlayer(tcgPrices);
        var cm = Cardmarket(pricing.Cardmarket, finish != CardFinish.Normal);

        // Cardmarket's foil fields cannot be split between holofoil and reverse holofoil, so they are only
        // offered for one of them when TCGplayer has no foil entry at all (then it is reported as holofoil).
        if (finish == CardFinish.ReverseHolofoil && pricing.TcgPlayer?.ReverseHolofoil is null)
            cm = null;
        if (finish == CardFinish.Holofoil && pricing.TcgPlayer?.Holofoil is null && pricing.TcgPlayer?.ReverseHolofoil is not null)
            cm = null;

        if (tcg is null && cm is null)
            return null;

        var tcgEur = tcg is { } usd && usdToEur is { } rate ? Math.Round(usd * rate, 2) : (decimal?)null;

        decimal? averageEur;
        var disagree = false;
        if (cm is { } a && tcgEur is { } b)
        {
            var mean = (a + b) / 2;
            disagree = !IsReliable(a, mean) || !IsReliable(b, mean);
            averageEur = disagree ? null : Round(mean);
        }
        else
        {
            averageEur = cm ?? tcgEur;
        }

        return new CalculatedAveragePrice(finish, assumed, cm, tcg, tcgEur, averageEur, disagree);
    }

    private static decimal? Cardmarket(CardmarketPricing? cm, bool holo)
    {
        if (cm is null)
            return null;

        var figures = holo
            ? new[] { cm.HoloTrend, cm.Avg7DaysHolo, cm.Avg30DaysHolo, cm.HoloAverage }
            : new[] { cm.Trend, cm.Avg7Days, cm.Avg30Days, cm.Average };

        return ReliableMean(figures, anchor: null) ?? Positive(holo ? cm.HoloLow : cm.Low);
    }

    private static decimal? TcgPlayer(TcgPlayerPrices? prices)
    {
        if (prices is null)
            return null;

        // Market (recent sales) is the reference; a mid listing far from it is dropped.
        return ReliableMean([prices.Market, prices.Mid], anchor: Positive(prices.Market)) ?? Positive(prices.Low);
    }

    /// <summary>Mean of the positive values that are within tolerance of the anchor (default: their median).</summary>
    private static decimal? ReliableMean(IEnumerable<decimal?> values, decimal? anchor)
    {
        var all = values.Select(Positive).OfType<decimal>().ToArray();
        if (all.Length == 0)
            return null;

        var reference = anchor ?? Median(all);
        var reliable = all.Where(v => IsReliable(v, reference)).ToArray();
        return reliable.Length == 0 ? null : Round(reliable.Average());
    }

    private static bool IsReliable(decimal value, decimal reference) =>
        Math.Abs(value - reference) <= Math.Max(AbsoluteTolerance, RelativeTolerance * reference);

    private static decimal Median(decimal[] values)
    {
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // The API reports 0 for "no data"; treat it like missing.
    private static decimal? Positive(decimal? value) => value is > 0 ? value : null;
}
