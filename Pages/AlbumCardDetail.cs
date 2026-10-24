using System.Globalization;
using CardTradeBuddy.Data;
using CardTradeBuddy.Services;

namespace CardTradeBuddy.Pages;

/// <summary>What the card popup on the album page shows: the saved card, the kroner rate on a Danish page, and an optional message (the result of an update).</summary>
/// <param name="EurToDkk">Danish kroner per euro on a Danish page; null means euros.</param>
public record AlbumCardDetail(Card Card, decimal? EurToDkk, string? Message = null, bool MessageIsError = false)
{
    /// <summary>A price in euros written in the page's currency ("12,34 kr." in Danish, "€12.34" otherwise).</summary>
    public string Money(decimal eur) => CardChecker.FormatMoney(eur, EurToDkk);
}
