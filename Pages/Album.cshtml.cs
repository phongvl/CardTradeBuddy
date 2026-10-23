using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace CardTradeBuddy.Pages;

/// <summary>
/// The album page: the user's cards as small thumbnails with their names, a page at a time. The default album is always the one
/// shown (the <c>album</c> address value is ready for "choose an album", there is no way to choose yet). The cards are not clickable
/// yet: showing a card's details comes later.
/// </summary>
public class AlbumModel(AlbumService albums, IConfiguration configuration, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>Cards per page when <c>Album:PageSize</c> is not set.</summary>
    public const int DefaultPageSize = 50;

    private const int MaxPageSize = 200;

    /// <summary>The page number, from 1 (<c>?p=2</c>).</summary>
    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    /// <summary>Which album to show; empty means the default album. Not offered in the page yet.</summary>
    [BindProperty(SupportsGet = true, Name = "album")]
    public Guid? AlbumId { get; set; }

    /// <summary>The album's name in the user's language.</summary>
    public string Title { get; private set; } = "";

    public int TotalCards { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public IReadOnlyList<AlbumCardItem> Cards { get; private set; } = [];

    /// <summary>The page numbers to link to: the first, the last and the ones around the current page; null is a gap ("...").</summary>
    public IReadOnlyList<int?> PageLinks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (UserPreferences.UserId(User) is not { } userId)
            return Challenge();

        // The page size is a setting (Album:PageSize, 50 by default), kept within a sensible range.
        var pageSize = Math.Clamp(configuration.GetValue("Album:PageSize", DefaultPageSize), 1, MaxPageSize);
        PageNumber = Math.Max(1, PageNumber);

        var page = await albums.GetPageAsync(userId, AlbumId, PageNumber, pageSize, cancellationToken);
        if (page is null)
            return NotFound();

        TotalCards = page.TotalCards;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCards / (double)pageSize));
        if (PageNumber > TotalPages)
            return RedirectToPage(new { p = TotalPages, album = AlbumId });

        Title = page.Album is null || page.Album.IsDefault ? localizer["DefaultAlbumName"].Value : page.Album.Name;
        Cards = page.Cards;
        PageLinks = BuildPageLinks(PageNumber, TotalPages);
        return Page();
    }

    /// <summary>The display name of a finish (for the tooltip and the screen reader text of the finish icon).</summary>
    public string FinishName(CardFinish finish) => finish switch
    {
        CardFinish.Holofoil => localizer["ResFinishHolo"].Value,
        CardFinish.ReverseHolofoil => localizer["ResFinishReverse"].Value,
        _ => localizer["ResFinishNormal"].Value,
    };

    /// <summary>The smaller picture of a card (TCGdex has several sizes: ".../high.webp" becomes ".../low.webp"), or the address as it is.</summary>
    public static string? ThumbnailUrl(string? imageUrl) => imageUrl?.Replace("/high.", "/low.", StringComparison.Ordinal);

    private static List<int?> BuildPageLinks(int current, int total)
    {
        var shown = new SortedSet<int> { 1, total };
        for (var n = current - 2; n <= current + 2; n++)
            if (n >= 1 && n <= total) shown.Add(n);

        var links = new List<int?>();
        var previous = 0;
        foreach (var n in shown)
        {
            if (n - previous > 1) links.Add(null);
            links.Add(n);
            previous = n;
        }

        return links;
    }
}
