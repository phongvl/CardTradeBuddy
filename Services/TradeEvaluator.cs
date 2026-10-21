namespace CardTradeBuddy.Services;

/// <summary>
/// Tuning knobs for <see cref="TradeEvaluator"/>. There is no official formula for a "fair" card trade;
/// these defaults encode common trading rules of thumb and are meant to be adjusted.
/// </summary>
public record TradeOptions
{
    /// <summary>Allowed difference, as a share of the trade, for very cheap trades.</summary>
    public decimal MaxPercent { get; init; } = 0.25m;

    /// <summary>Allowed difference, as a share of the trade, that very expensive trades shrink towards.</summary>
    public decimal MinPercent { get; init; } = 0.05m;

    /// <summary>Trade value (EUR) at which the allowed percentage is halfway between max and min.</summary>
    public decimal HalfwayValue { get; init; } = 50m;

    /// <summary>A difference up to this many EUR is always fair, however small the cards are.</summary>
    public decimal MinAbsolute { get; init; } = 1.00m;

    /// <summary>Discount on the side with more cards, per extra card (many small cards are harder to sell on than one big one).</summary>
    public decimal ConsolidationPerExtraCard { get; init; } = 0.03m;

    /// <summary>Upper limit of that discount.</summary>
    public decimal MaxConsolidation { get; init; } = 0.15m;
}

public enum TradeVerdict
{
    Fair,
    FavorsYou,
    FavorsThem,
}

/// <param name="YouReceive">Nominal value of the cards you get.</param>
/// <param name="YouGive">Nominal value of the cards you hand over.</param>
/// <param name="YouReceiveEffective">Value of what you get, after the consolidation discount if that side has more cards.</param>
/// <param name="YouGiveEffective">Value of what you give, after the consolidation discount if that side has more cards.</param>
/// <param name="Difference">Effective value received minus effective value given; positive means the trade favors you.</param>
/// <param name="DifferencePercent">The difference as a share of the larger effective side.</param>
/// <param name="AllowedDifference">Largest absolute difference (EUR) still counted as fair.</param>
/// <param name="AllowedPercent">The same limit as a share of the larger effective side.</param>
public record TradeResult(
    decimal YouReceive,
    decimal YouGive,
    decimal YouReceiveEffective,
    decimal YouGiveEffective,
    decimal Difference,
    decimal DifferencePercent,
    decimal AllowedDifference,
    decimal AllowedPercent,
    TradeVerdict Verdict)
{
    public bool IsFair => Verdict == TradeVerdict.Fair;
}

public static class TradeEvaluator
{
    /// <summary>
    /// Judges a trade from the cards' prices (EUR each).
    /// <list type="number">
    /// <item><b>Consolidation:</b> the side with more cards is discounted by <c>ConsolidationPerExtraCard</c> for every
    /// extra card (capped), because many small cards are worth less to the person who has to resell them than one
    /// card of the same total price.</item>
    /// <item><b>Tolerance shrinks with value:</b> the allowed difference is
    /// <c>MinPercent + (MaxPercent - MinPercent) / (1 + V / HalfwayValue)</c> of the larger effective side <c>V</c>.
    /// Cheap trades get a wide percentage, expensive ones a narrow one. The allowed amount in EUR still grows with
    /// the trade, and never drops below <c>MinAbsolute</c>.</item>
    /// <item><b>Verdict:</b> fair when the difference is within that allowance, otherwise it says who it favors.</item>
    /// </list>
    /// </summary>
    public static TradeResult Evaluate(IReadOnlyCollection<decimal> youGive, IReadOnlyCollection<decimal> youReceive, TradeOptions? options = null)
    {
        options ??= new TradeOptions();

        var give = youGive.Sum();
        var receive = youReceive.Sum();

        var giveEffective = give * (1 - ConsolidationDiscount(youGive.Count - youReceive.Count, options));
        var receiveEffective = receive * (1 - ConsolidationDiscount(youReceive.Count - youGive.Count, options));

        var larger = Math.Max(giveEffective, receiveEffective);
        var allowedPercent = options.MinPercent + (options.MaxPercent - options.MinPercent) / (1 + larger / options.HalfwayValue);
        var allowed = Math.Max(options.MinAbsolute, larger * allowedPercent);

        var difference = receiveEffective - giveEffective;
        var verdict = Math.Abs(difference) <= allowed
            ? TradeVerdict.Fair
            : difference > 0 ? TradeVerdict.FavorsYou : TradeVerdict.FavorsThem;

        return new TradeResult(
            Round(receive), Round(give), Round(receiveEffective), Round(giveEffective),
            Round(difference),
            larger == 0 ? 0 : Math.Round(difference / larger, 4),
            Round(allowed),
            larger == 0 ? 0 : Math.Round(allowed / larger, 4),
            verdict);
    }

    // Only the side with more cards is discounted, by how many more it has.
    private static decimal ConsolidationDiscount(int extraCards, TradeOptions options) =>
        extraCards <= 0 ? 0 : Math.Min(options.MaxConsolidation, extraCards * options.ConsolidationPerExtraCard);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
