namespace CardTradeBuddy.Services;

public interface ICardDataProvider
{
    /// <param name="setCode">Optional printed set code (e.g. "DRI"), used to pick the right set when the name and number exist in several.</param>
    /// <param name="languageCode">Optional printed language code (EN, DE, FR, IT, ES, PT, JA). Defaults to English.</param>
    /// <param name="setTotal">Optional total after the slash in the card number (the 182 in 001/182). Together with
    /// <paramref name="setCode"/> it picks the right set when the name and number exist in several, and a mismatch is
    /// reported in <see cref="CardData.MatchNote"/>.</param>
    Task<CardData?> GetCardByNameAndIdAsync(string name, string localId, string? setCode = null, string? languageCode = null, int? setTotal = null);
}
