using CardTradeBuddy.Data;
using Microsoft.EntityFrameworkCore;

namespace CardTradeBuddy.Services;

/// <param name="Album">The album the card went into.</param>
/// <param name="Card">The saved card row.</param>
/// <param name="AlreadyThere">True when the same card was added a moment ago (a double press or a page refresh), so nothing new was saved.</param>
public record AddedCard(Album Album, Card Card, bool AlreadyThere);

/// <summary>One card in an album list: only what the thumbnail grid needs (the heavy price data is left out).</summary>
public record AlbumCardItem(Guid Id, string Name, string? ImageUrl, CardFinish Finish);

/// <param name="Album">The album shown, or null when the user has none yet (nothing is created just by looking).</param>
/// <param name="TotalCards">How many cards the album holds in all (not only on this page).</param>
/// <param name="Cards">The cards of the requested page, newest first.</param>
public record AlbumPage(Album? Album, int TotalCards, IReadOnlyList<AlbumCardItem> Cards);

/// <summary>
/// A user's albums and the cards in them. Every user has one default album, created the first time it is needed. Users cannot make
/// more albums yet: <see cref="AddCardAsync"/> already takes an album, so a later "choose an album" only has to pass it in.
/// </summary>
public class AlbumService(AppDbContext db, ILogger<AlbumService> log)
{
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
        var items = await cards
            .OrderByDescending(c => c.AddedAt).ThenBy(c => c.Id)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(c => new AlbumCardItem(c.Id, c.Name, c.ImageUrl, c.Finish))
            .ToListAsync(ct);
        return new AlbumPage(album, total, items);
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
