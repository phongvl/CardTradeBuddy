using CardTradeBuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace CardTradeBuddy.Services;

/// <param name="Album">The album the card went into.</param>
/// <param name="Card">The saved card row.</param>
/// <param name="AlreadyThere">True when the same card was added a moment ago (a double press or a page refresh), so nothing new was saved.</param>
public record AddedCard(Album Album, Card Card, bool AlreadyThere);

public enum RefreshOutcome
{
    /// <summary>The card's details and price were replaced with the latest.</summary>
    Updated,

    /// <summary>The card list did not have this card any more (or the lookup gave another card): nothing was changed.</summary>
    NotFound,

    /// <summary>The card list or the exchange rate could not be reached: nothing was changed.</summary>
    Failed,
}

/// <param name="Total">How many cards the album holds.</param>
/// <param name="Processed">How many cards this batch looked at.</param>
public record RefreshBatch(int Total, int Processed, int Updated, int NotFound, int Failed);

/// <summary>One card in an album list: only what the thumbnail grid needs (the heavy price data is left out).</summary>
public record AlbumCardItem(Guid Id, string Name, string? ImageUrl, CardFinish Finish, decimal? EstimatedPriceEur, DateTime PricesCheckedAt);

/// <param name="Album">The album shown, or null when the user has none yet (nothing is created just by looking).</param>
/// <param name="TotalCards">How many cards the album holds in all (not only on this page).</param>
/// <param name="Cards">The cards of the requested page, newest first.</param>
/// <param name="TotalValueEur">The estimated prices of ALL the cards in the album added up, in euros (not only the cards of this page).</param>
/// <param name="CardsWithoutPrice">How many cards in the album have no price and so are not in the total.</param>
public record AlbumPage(Album? Album, int TotalCards, IReadOnlyList<AlbumCardItem> Cards, decimal TotalValueEur = 0, int CardsWithoutPrice = 0);

/// <summary>
/// A user's albums and the cards in them. Every user has one default album, created the first time it is needed. Users cannot make
/// more albums yet: <see cref="AddCardAsync"/> already takes an album, so a later "choose an album" only has to pass it in.
/// </summary>
public class AlbumService(AppDbContext db, ICardDataProvider provider, IExchangeRateService rates, ILogger<AlbumService> log)
{
    /// <summary>How many cards of a batch are looked up at the same time.</summary>
    private const int RefreshParallelism = 3;

    /// <summary>A card added twice within this time is taken to be the same press (double click, refresh of the page).</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(30);

    /// <summary>The user's default album, created if they have none.</summary>
    public async Task<Album> GetOrCreateDefaultAsync(string userId, CancellationToken ct = default)
    {
        var album = await db.Albums.SingleOrDefaultAsync(a => a.UserId == userId && a.IsDefault, ct);
        if (album is not null) return album;

        album = new Album { Id = Guid.NewGuid(), UserId = userId, Name = Album.DefaultName, IsDefault = true, CreatedAt = DateTime.UtcNow };
        db.Albums.Add(album);
        try
        {
            await db.SaveChangesAsync(ct);
            return album;
        }
        catch (DbUpdateException ex)
        {
            // Two requests creating the first album at once: the unique index let one through, use that one.
            log.LogInformation(ex, "The default album of {UserId} was created by another request.", userId);
            db.Entry(album).State = EntityState.Detached;
            return await db.Albums.SingleAsync(a => a.UserId == userId && a.IsDefault, ct);
        }
    }

    /// <summary>
    /// One page of an album's cards, newest first: <paramref name="albumId"/>, or the default album when it is null.
    /// Looking never creates an album: a user who has none gets an empty page. An album that is not the user's gives null.
    /// </summary>
    public async Task<AlbumPage?> GetPageAsync(string userId, Guid? albumId, int page, int pageSize, CancellationToken ct = default)
    {
        var album = albumId is { } id
            ? await db.Albums.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
            : await db.Albums.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId && a.IsDefault, ct);
        if (album is null)
            return albumId is null ? new AlbumPage(null, 0, []) : null;

        var cards = db.Cards.AsNoTracking().Where(c => c.AlbumId == album.Id);
        var total = await cards.CountAsync(ct);
        // The value of the whole album, whatever page is shown. Cards without a price count as nothing.
        var value = await cards.SumAsync(c => c.EstimatedPriceEur ?? 0m, ct);
        var withoutPrice = await cards.CountAsync(c => c.EstimatedPriceEur == null, ct);
        var items = await cards
            .OrderByDescending(c => c.AddedAt).ThenBy(c => c.Id)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(c => new AlbumCardItem(c.Id, c.Name, c.ImageUrl, c.Finish, c.EstimatedPriceEur, c.PricesCheckedAt))
            .ToListAsync(ct);
        return new AlbumPage(album, total, items, value, withoutPrice);
    }

    /// <summary>One card of the user's, with all its data, or null when it does not exist or is not the user's.</summary>
    public Task<Card?> GetCardAsync(string userId, Guid cardId, CancellationToken ct = default) =>
        db.Cards.Include(c => c.Album).SingleOrDefaultAsync(c => c.Id == cardId && c.Album.UserId == userId, ct);

    /// <summary>
    /// Looks the card up again by its name and number (and its language, and the set's card total to tell sets apart) and replaces its saved
    /// details and price with what the card list says now. The user's chosen finish stays; the price is recalculated for it. The card is
    /// only changed when the lookup finds the very same card (same TCGdex id): a different card, or none, leaves it untouched.
    /// </summary>
    public async Task<RefreshOutcome> RefreshCardAsync(Card card, CancellationToken ct = default)
    {
        var usdToEur = await rates.GetUsdToEurAsync(ct);
        if (usdToEur is null) return RefreshOutcome.Failed;

        var fresh = await FindAsync(card);
        return await ApplyAsync(card, fresh, usdToEur.Value, ct);
    }

    /// <summary>
    /// Refreshes a batch of an album's cards (newest first, <paramref name="skip"/> and <paramref name="take"/>), a few at a time. The page
    /// calls this again and again to refresh a whole album while it shows the progress. Returns null when the album is not the user's.
    /// </summary>
    public async Task<RefreshBatch?> RefreshBatchAsync(string userId, Guid? albumId, int skip, int take, CancellationToken ct = default)
    {
        var album = albumId is { } id
            ? await db.Albums.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
            : await db.Albums.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId && a.IsDefault, ct);
        if (album is null) return albumId is null ? new RefreshBatch(0, 0, 0, 0, 0) : null;

        var total = await db.Cards.CountAsync(c => c.AlbumId == album.Id, ct);
        var batch = await db.Cards.Where(c => c.AlbumId == album.Id)
            .OrderByDescending(c => c.AddedAt).ThenBy(c => c.Id)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 20)).ToListAsync(ct);

        var usdToEur = await rates.GetUsdToEurAsync(ct);
        if (usdToEur is null) return new RefreshBatch(total, batch.Count, 0, 0, batch.Count);

        // The lookups go out a few at a time; the changes are saved one after the other (a DbContext is not for several threads).
        using var gate = new SemaphoreSlim(RefreshParallelism);
        var found = await Task.WhenAll(batch.Select(async c =>
        {
            await gate.WaitAsync(ct);
            try { return (Card: c, Fresh: await FindAsync(c)); }
            finally { gate.Release(); }
        }));

        int updated = 0, notFound = 0, failed = 0;
        foreach (var (card, fresh) in found)
        {
            switch (await ApplyAsync(card, fresh, usdToEur.Value, ct))
            {
                case RefreshOutcome.Updated: updated++; break;
                case RefreshOutcome.NotFound: notFound++; break;
                default: failed++; break;
            }
        }

        return new RefreshBatch(total, batch.Count, updated, notFound, failed);
    }

    /// <summary>The card list's answer for this card, found by name and number; null when it was not found. A failed lookup throws <see cref="HttpRequestException"/> and the like.</summary>
    private async Task<CardData?> FindAsync(Card card)
    {
        try
        {
            return await provider.GetCardByNameAndIdAsync(card.Name, card.LocalId, setCode: null, languageCode: card.Language, setTotal: card.SetOfficialCardCount);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            log.LogWarning(ex, "Could not look up {Name} {LocalId} again.", card.Name, card.LocalId);
            return FailedLookup;
        }
    }

    private static readonly CardData FailedLookup = new() { Id = "", LocalId = "", Name = "" };

    private async Task<RefreshOutcome> ApplyAsync(Card card, CardData? fresh, decimal usdToEur, CancellationToken ct)
    {
        if (ReferenceEquals(fresh, FailedLookup)) return RefreshOutcome.Failed;
        // Nothing, or another card with the same name and number (in another set): leave the saved card as it is.
        if (fresh is null || !string.Equals(fresh.Id, card.CardId, StringComparison.OrdinalIgnoreCase)) return RefreshOutcome.NotFound;

        card.LocalId = fresh.LocalId;
        card.Name = fresh.Name;
        card.Language = fresh.Language;
        card.Category = fresh.Category;
        card.Rarity = fresh.Rarity;
        card.Illustrator = fresh.Illustrator;
        card.Description = fresh.Description;
        card.SetId = fresh.SetId;
        card.SetName = fresh.SetName;
        card.SetOfficialCardCount = fresh.SetOfficialCardCount;
        card.ImageUrl = fresh.ImageUrl;
        card.SetLogoUrl = fresh.SetLogoUrl;
        card.Hp = fresh.Hp;
        card.Stage = fresh.Stage;
        card.Types = [.. fresh.Types];
        card.Retreat = fresh.Retreat;
        card.Pricing = fresh.Pricing;

        // The price for the finish the user said they have.
        card.EstimatedPriceEur = PriceCalculator.CalculateAll(fresh.Pricing, usdToEur).FirstOrDefault(p => p.Finish == card.Finish)?.AverageEur;
        card.PricesCheckedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return RefreshOutcome.Updated;
    }

    /// <summary>
    /// Puts a found card in an album of the user: <paramref name="albumId"/>, or the default album when it is null.
    /// Returns null when the album does not exist or is not the user's.
    /// <paramref name="finish"/> is the version the user says they have (normal, holofoil or reverse holofoil): the card is saved with it
    /// and with the price calculated for exactly that finish.
    /// </summary>
    public async Task<AddedCard?> AddCardAsync(string userId, Guid? albumId, CardCheckResult result, CardFinish finish, CancellationToken ct = default)
    {
        if (finish is not (CardFinish.Normal or CardFinish.Holofoil or CardFinish.ReverseHolofoil))
            throw new ArgumentException("The finish must be normal, holofoil or reverse holofoil.", nameof(finish));

        if (result is not { Outcome: CardCheckOutcome.Found, Card: { } data })
            throw new ArgumentException("Only a found card can be added.", nameof(result));

        Album? album = albumId is { } id
            ? await db.Albums.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
            : await GetOrCreateDefaultAsync(userId, ct);
        if (album is null) return null;

        var now = DateTime.UtcNow;
        // The price for the finish the user chose (null when there is no price data for that version).
        var price = result.Prices?.FirstOrDefault(p => p.Finish == finish);

        var recent = await db.Cards.FirstOrDefaultAsync(
            c => c.AlbumId == album.Id && c.CardId == data.Id && c.Finish == finish && c.AddedAt > now - DuplicateWindow, ct);
        if (recent is not null) return new AddedCard(album, recent, AlreadyThere: true);

        var card = new Card
        {
            Id = Guid.NewGuid(),
            AlbumId = album.Id,
            AddedAt = now,
            CardId = data.Id,
            LocalId = data.LocalId,
            Name = data.Name,
            Language = data.Language,
            Category = data.Category,
            Rarity = data.Rarity,
            Illustrator = data.Illustrator,
            Description = data.Description,
            SetId = data.SetId,
            SetName = data.SetName,
            SetOfficialCardCount = data.SetOfficialCardCount,
            ImageUrl = data.ImageUrl,
            SetLogoUrl = data.SetLogoUrl,
            Hp = data.Hp,
            Stage = data.Stage,
            Types = [.. data.Types],
            Retreat = data.Retreat,
            Finish = finish,
            FinishAssumed = false,
            EstimatedPriceEur = price?.AverageEur,
            Pricing = data.Pricing,
            PricesCheckedAt = result.CheckedAt ?? now,
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
        return new AddedCard(album, card, AlreadyThere: false);
    }
}
