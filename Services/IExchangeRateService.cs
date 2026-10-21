namespace CardTradeBuddy.Services;

public interface IExchangeRateService
{
    /// <summary>Euros per one US dollar, or null when the rate cannot be fetched.</summary>
    Task<decimal?> GetUsdToEurAsync(CancellationToken cancellationToken = default);
}
