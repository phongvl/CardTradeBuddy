using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CardTradeBuddy.Services;

public enum CardCheckOutcome
{
    /// <summary>The card was recognised and found in the card list.</summary>
    Found,

    /// <summary>No usable image (missing, too big, not an image the app can read).</summary>
    BadImage,

    NotAPokemonCard,

    /// <summary>The card still looked upside down after turning the photo.</summary>
    UpsideDown,

    /// <summary>It is a Pokémon card, but the name or the number could not be read.</summary>
    Unreadable,

    /// <summary>The card was read but is not in the card list.</summary>
    NotInDatabase,

    /// <summary>The model or the card list could not be reached.</summary>
    Unavailable,
}

/// <param name="Name">The name read from the card (also set when the card was not found, for the message).</param>
/// <param name="Price">The estimate for the detected finish, or for the assumed one when the finish is unknown.</param>
/// <param name="Prices">The estimate for every finish that has price data.</param>
/// <param name="EurToDkk">Danish kroner per euro, when kroner were asked for and the rate was available.</param>
/// <param name="CheckedAt">When the card and its prices were looked up (UTC); set when the card was found.</param>
public record CardCheckResult(
    CardCheckOutcome Outcome,
    CardData? Card = null,
    string? Name = null,
    CardFinish Finish = CardFinish.Unknown,
    CalculatedAveragePrice? Price = null,
    IReadOnlyList<CalculatedAveragePrice>? Prices = null,
    decimal? EurToDkk = null,
    DateTime? CheckedAt = null);

/// <summary>
/// The "check a card" pipeline for the Check page: photo in, card and price out. Same steps as the test page
/// (<c>ProtoUpload</c>): prepare the photo, ask the vision model, look the card up on TCGdex, calculate the price.
/// Nothing is stored. The prompt is kept in step with the one on ProtoUpload.
/// </summary>
public class CardChecker(ILlmService llm, ICardDataProvider cards, IExchangeRateService rates, IConfiguration configuration, ILogger<CardChecker> log)
{
    /// <summary>The biggest photo accepted (phone photos are 2-8 MB; the page shrinks them before sending anyway).</summary>
    public const long MaxPhotoBytes = 12 * 1024 * 1024;

    private const string DefaultModel = "qwen3-vl:8b";

    private const string CardPrompt = """
        FIRST check the orientation. If the text in the image is upside down, reply with only
        {"upsideDown": true} and do not read anything else.

        This image was made from a photo of a card by stacking three parts, top to bottom: the top strip
        of the card (name, HP, type), a small thumbnail of the artwork, and the bottom strip (set code,
        language, card number). The middle of the card (attacks and text) was left out on purpose.

        Decide whether it is a Pokémon trading card. If it is, read the card name from the top strip and
        the card number from the bottom strip.

        The bottom-left of the card shows a small box with a 3-letter set code
        (uppercase letters only, e.g. DRI, SVI, MEW), immediately followed by a
        2-letter language code (EN, DE, FR, IT, ES, PT, JA). They are printed
        together but are two separate fields. Then the card number, e.g. 001/182.
        Do not correct them into real words.

        Also try to tell which finish this particular card has, from the artwork thumbnail and the
        borders and text areas:
        - "holofoil": the artwork window itself is shiny or rainbow foil.
        - "reverseHolofoil": the artwork is flat and matte, but the rest of the card
          (border, text box, background) has a shiny foil pattern.
        - "normal": no foil effects anywhere.
        A photo often cannot show foil reliably. If you cannot tell, use "unknown".
        Do not guess.

        Otherwise reply with JSON only, in exactly this shape:
        {
          "isPokemonCard": true or false,
          "upsideDown": true or false,
          "name": "card name as printed, or null",
          "setCode": "3-letter set code, or null",
          "languageCode": "2-letter language code, or null",
          "cardNumber": "number as printed, e.g. 001/182, or null",
          "localId": "the part of the card number before the slash, e.g. 001, or null",
          "finish": "normal", "holofoil", "reverseHolofoil" or "unknown"
        }
        """;

    private static readonly string[] LanguageCodes = ["EN", "DE", "FR", "IT", "ES", "PT", "JA"];

    /// <param name="photo">The uploaded photo.</param>
    /// <param name="inKroner">True on a Danish page: the result then carries the EUR -> DKK rate.</param>
    public async Task<CardCheckResult> CheckAsync(IFormFile? photo, bool inKroner, CancellationToken cancellationToken = default)
    {
        if (photo is null || photo.Length == 0 || photo.Length > MaxPhotoBytes)
            return new CardCheckResult(CardCheckOutcome.BadImage);

        byte[] bytes;
        await using (var stream = photo.OpenReadStream())
        {
            if (!await IsSupportedImageAsync(stream))
                return new CardCheckResult(CardCheckOutcome.BadImage);

            stream.Position = 0;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        try
        {
            var model = await ChooseModelAsync(cancellationToken);
            if (model is null)
                return new CardCheckResult(CardCheckOutcome.Unavailable);

            var result = await ReadCardAsync(bytes, rotate180: false, model, cancellationToken);

            // A card photographed lying sideways can come out upside down; only the model can tell, so flip and ask again.
            if (GetBool(result, "upsideDown"))
                result = await ReadCardAsync(bytes, rotate180: true, model, cancellationToken);

            if (GetBool(result, "upsideDown"))
                return new CardCheckResult(CardCheckOutcome.UpsideDown);
            if (!GetBool(result, "isPokemonCard"))
                return new CardCheckResult(CardCheckOutcome.NotAPokemonCard);

            var finish = GetString(result, "finish")?.ToLowerInvariant() switch
            {
                "normal" => CardFinish.Normal,
                "holofoil" => CardFinish.Holofoil,
                "reverseholofoil" => CardFinish.ReverseHolofoil,
                _ => CardFinish.Unknown,
            };

            var name = GetString(result, "name");
            var setCode = GetString(result, "setCode")?.ToUpperInvariant() is { } s && Regex.IsMatch(s, "^[A-Z0-9]{2,5}$") ? s : null;
            var languageCode = GetString(result, "languageCode")?.ToUpperInvariant() is { } l && LanguageCodes.Contains(l) ? l : null;
            var cardNumber = NormalizeNumber(GetString(result, "cardNumber"));
            var localId = cardNumber?.Split('/')[0] ?? GetString(result, "localId");
            if (name is null || localId is null)
                return new CardCheckResult(CardCheckOutcome.Unreadable);

            // The total after the slash (the 182 in 001/182) identifies the set, so it cross-checks the set code.
            var total = int.TryParse(cardNumber?.Split('/').ElementAtOrDefault(1), out var t) ? t : (int?)null;

            var card = await cards.GetCardByNameAndIdAsync(name, localId, setCode, languageCode, total);
            if (card is null)
                return new CardCheckResult(CardCheckOutcome.NotInDatabase, Name: name);

            var usdToEur = await rates.GetUsdToEurAsync(cancellationToken);
            var eurToDkk = inKroner ? await rates.GetEurToDkkAsync(cancellationToken) : null;
            return new CardCheckResult(
                CardCheckOutcome.Found,
                card,
                name,
                finish,
                PriceCalculator.Calculate(card.Pricing, finish, usdToEur),
                PriceCalculator.CalculateAll(card.Pricing, usdToEur),
                eurToDkk,
                DateTime.UtcNow);
        }
        catch (OpenCvSharp.OpenCVException ex)
        {
            log.LogInformation(ex, "A photo could not be decoded.");
            return new CardCheckResult(CardCheckOutcome.BadImage);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or JsonException)
        {
            log.LogError(ex, "Checking a card failed.");
            return new CardCheckResult(CardCheckOutcome.Unavailable);
        }
    }

    /// <summary>The configured model (<c>Check:Model</c>, default qwen3-vl:8b) when it is installed, otherwise the first model that can read images.</summary>
    private async Task<string?> ChooseModelAsync(CancellationToken cancellationToken)
    {
        var models = await llm.GetVisionModelsAsync(cancellationToken);
        if (models.Count == 0) return null;

        var wanted = configuration["Check:Model"] ?? DefaultModel;
        return models.Contains(wanted) ? wanted : models[0];
    }

    private async Task<JsonElement> ReadCardAsync(byte[] photo, bool rotate180, string model, CancellationToken cancellationToken)
    {
        var prepared = CardImagePreprocessor.PrepareForReading(photo, rotate180);
        using var image = new MemoryStream(prepared.Jpeg);
        return await llm.AskAboutImageAsync(image, CardPrompt, model, cancellationToken);
    }

    // Models may wrap a boolean in quotes; accept both true and "true".
    private static bool GetBool(JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var b) && b));

    private static string? NormalizeNumber(string? text)
    {
        var s = text is null ? null : string.Concat(text.Where(ch => !char.IsWhiteSpace(ch))).ToUpperInvariant();
        return s is not null && Regex.IsMatch(s, "^[A-Z0-9]{1,6}(/[A-Z0-9]{1,6})?$") ? s : null;
    }

    // Models return null, "null" or "" for fields they could not read; treat all as missing.
    private static string? GetString(JsonElement json, string property)
    {
        if (!json.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) || text.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : text;
    }

    // Checks the file's leading bytes (magic numbers) rather than trusting the file name or the client-supplied Content-Type.
    private static async Task<bool> IsSupportedImageAsync(Stream stream)
    {
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        var h = header.AsSpan(0, read);

        return h.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })
            || h.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
            || h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8)
            || h.StartsWith("BM"u8)
            || (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8));
    }

    /// <summary>A price (kept in euros) in the page's currency: "12,34 kr." with the kroner rate, "€12.34" without.</summary>
    public static string FormatMoney(decimal eur, decimal? eurToDkk) => eurToDkk is { } rate
        ? string.Format(CultureInfo.CurrentCulture, "{0:N2} kr.", eur * rate)
        : string.Format(CultureInfo.CurrentCulture, "€{0:0.00}", eur);
}
