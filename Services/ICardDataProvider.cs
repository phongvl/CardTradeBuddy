namespace CardTradeBuddy.Services;

public interface ICardDataProvider
{    
    Task<CardData?> GetCardByNameAndIdAsync(string name, string localId);
}