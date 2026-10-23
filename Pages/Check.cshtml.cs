using System.Globalization;
using System.Text.Json;
using CardTradeBuddy.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace CardTradeBuddy.Pages;

/// <summary>
/// "Check a card": the user takes or uploads a photo, the card is recognised and its details and estimated price are shown.
/// For logged-in users only (the model uses the GPU), and limited per user (see the "scan" rate limit in Program.cs).
/// The photo is read in memory and never saved.
/// </summary>
[EnableRateLimiting("scan")]
[RequestSizeLimit(16 * 1024 * 1024)]
public class CheckModel(CardChecker checker, AlbumService albums, IDataProtectionProvider protection, IStringLocalizer<SharedResource> localizer) : PageModel
{
    /// <summary>How long a result can be added to an album after it was shown (the page keeps the result in a signed field).</summary>
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _results = protection.CreateProtector("CardTradeBuddy.CheckResult.v1").ToTimeLimitedDataProtector();

    /// <summary>The photo, put there by the page's script (a single file field named Photo).</summary>
    [BindProperty]
    public IFormFile? Photo { get; set; }

    public CardCheckResult? Result { get; private set; }

    /// <summary>The result, signed and sealed, posted back by the "Add to album" button. The user cannot change what is added.</summary>
    public string? Token { get; private set; }

    /// <summary>The name of the album the card was just added to (in the user's language); null before it is added.</summary>
    public string? AddedTo { get; private set; }

    /// <summary>The version of the card that was just added, as a display name.</summary>
    public string? AddedFinish { get; private set; }

    /// <summary>Why the photo could not be checked, in the user's language; null when there is nothing to say or the card was found.</summary>
    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // Danish page: prices in kroner.
        var inKroner = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "da";
        var result = await checker.CheckAsync(Photo, inKroner, cancellationToken);

        Error = result.Outcome switch
        {
            CardCheckOutcome.Found => null,
            CardCheckOutcome.BadImage => localizer["CheckErrBadImage", CardChecker.MaxPhotoBytes / (1024 * 1024)].Value,
            CardCheckOutcome.NotAPokemonCard => localizer["CheckErrNotCard"].Value,
            CardCheckOutcome.UpsideDown => localizer["CheckErrUpsideDown"].Value,
            CardCheckOutcome.Unreadable => localizer["CheckErrUnreadable"].Value,
            CardCheckOutcome.NotInDatabase => localizer["CheckErrNotFound", result.Name ?? ""].Value,
            _ => localizer["CheckErrUnavailable"].Value,
        };
        Result = result.Outcome == CardCheckOutcome.Found ? result : null;
        if (Result is not null) Token = _results.Protect(JsonSerializer.Serialize(Result), ResultLifetime);
        return Page();
    }

    /// <summary>"Add to album": puts the card that was found in the user's default album (created if they have none).</summary>
    public async Task<IActionResult> OnPostAddAsync(string? token, Guid? albumId, CardFinish? finish, CancellationToken cancellationToken)
    {
        if (UserPreferences.UserId(User) is not { } userId)
            return Challenge();

        CardCheckResult? result = null;
        try
        {
            if (!string.IsNullOrEmpty(token))
                result = JsonSerializer.Deserialize<CardCheckResult>(_results.Unprotect(token));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            // Too old, or not made by this site.
        }

        if (result is not { Outcome: CardCheckOutcome.Found })
        {
            Error = localizer["CheckErrExpired"].Value;
            return Page();
        }

        Result = result;
        Token = token;

        // The user must say which version of the card they have.
        if (finish is not (CardFinish.Normal or CardFinish.Holofoil or CardFinish.ReverseHolofoil))
        {
            Error = localizer["FinishRequired"].Value;
            return Page();
        }

        try
        {
            // No album named: the default one. (Choosing an album is for later; the album id is already passed on.)
            var added = await albums.AddCardAsync(userId, albumId, result, finish.Value, cancellationToken);
            if (added is null)
            {
                Error = localizer["AlbumErrUnavailable"].Value;
                return Page();
            }

            AddedTo = added.Album.IsDefault ? localizer["DefaultAlbumName"].Value : added.Album.Name;
            AddedFinish = FinishName(finish.Value);
        }
        catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<CheckModel>>().LogError(ex, "Could not add a card to an album.");
            Error = localizer["AlbumErrUnavailable"].Value;
        }

        return Page();
    }

    /// <summary>A price (kept in euros) written in the page's currency.</summary>
    public string Money(decimal eur) => CardChecker.FormatMoney(eur, Result?.EurToDkk);

    /// <summary>The display name of a finish.</summary>
    public string FinishName(CardFinish finish) => finish switch
    {
        CardFinish.Holofoil => localizer["ResFinishHolo"].Value,
        CardFinish.ReverseHolofoil => localizer["ResFinishReverse"].Value,
        _ => localizer["ResFinishNormal"].Value,
    };
}
