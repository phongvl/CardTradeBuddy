using Microsoft.Extensions.Caching.Memory;

namespace CardTradeBuddy.Services;

/// <summary>European Central Bank reference rates via the free, key-less Frankfurter API.</summary>
public class FrankfurterExchangeRateService(HttpClient http, IMemoryCache cache) : IExchangeRateService
{
    public const string BaseUrl = "https://api.frankfurter.dev/v1/";

    // The ECB publishes once per working day, so there is no point asking more often.
    private static readonly TimeSpan CacheTime = TimeSpan.FromHours(6);

    public async Task<decimal?> GetUsdToEurAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue("usd-eur", out decimal cached))
            return cached;

        try
        {
            var response = await http.GetFromJsonAsync<RateResponse>("latest?base=USD&symbols=EUR", cancellationToken);
            if (response?.Rates is { } rates && rates.TryGetValue("EUR", out var rate) && rate > 0)
            {
                cache.Set("usd-eur", rate, CacheTime);
                return rate;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Prices still work without a rate, they just are not converted.
        }

        return null;
    }

    private record RateResponse(Dictionary<string, decimal>? Rates);
}
