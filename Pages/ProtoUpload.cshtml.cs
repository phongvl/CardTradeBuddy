using System.Text.Json;
using System.Text.RegularExpressions;
using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CardTradeBuddy.Pages;

public class ProtoUploadModel(ILlmService llm, ICardDataProvider cards, IExchangeRateService rates) : PageModel
{
    private const string DefaultModel = "qwen3-vl:8b";

    // The model does not get the photo itself but an image assembled by CardImagePreprocessor, so the
    // small print is large and readable and the pixel count (and with it the time) stays low.
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

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [BindProperty]
    public IFormFile? Upload { get; set; }

    /// <summary>The Ollama model chosen in the dropdown.</summary>
    [BindProperty]
    public string? SelectedModel { get; set; }

    /// <summary>Installed Ollama models that can read images, for the dropdown.</summary>
    public List<SelectListItem> ModelItems { get; private set; } = [];

    /// <summary>Model that produced the result and how long it took, for comparing models.</summary>
    public string? ModelUsed { get; private set; }

    public double? ElapsedSeconds { get; private set; }

    /// <summary>Whether the card's outline was found in the photo (otherwise the whole photo was used).</summary>
    public bool? CardDetected { get; private set; }

    /// <summary>The image that was actually sent to the model, as a data URI, for checking what it saw.</summary>
    public string? PreparedImageDataUri { get; private set; }

    public string? Message { get; private set; }

    public string? ResultJson { get; private set; }

    public CardData? Card { get; private set; }

    /// <summary>Finish read from the image; "unknown holofoil" when the model could not tell.</summary>
    public string? DetectedFinish { get; private set; }

    /// <summary>The estimate for the detected finish (or the assumed one when the finish is unknown).</summary>
    public CalculatedAveragePrice? AveragePrice { get; private set; }

    /// <summary>The estimate for every finish that has price data.</summary>
    public IReadOnlyList<CalculatedAveragePrice> AveragePrices { get; private set; } = [];

    /// <summary>Euros per US dollar used for the conversion; null when the rate was unavailable.</summary>
    public decimal? UsdToEur { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadModelsAsync(cancellationToken);
    }

    private async Task LoadModelsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> models;
        try
        {
            models = await llm.GetVisionModelsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Message = $"Could not read the model list from Ollama: {ex.Message}";
            return;
        }

        if (models.Count == 0)
        {
            Message = "Ollama has no models that can read images.";
            return;
        }

        if (SelectedModel is null || !models.Contains(SelectedModel))
            SelectedModel = models.Contains(DefaultModel) ? DefaultModel : models[0];

        ModelItems = models.Select(m => new SelectListItem(m, m, m == SelectedModel)).ToList();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadModelsAsync(cancellationToken);
        if (ModelItems.Count == 0)
            return Page();

        if (Upload is null || Upload.Length == 0)
        {
            Message = "Please choose a file.";
            return Page();
        }

        byte[] photo;
        await using (var stream = Upload.OpenReadStream())
        {
            var format = await DetectImageFormatAsync(stream);
            if (format is null)
            {
                Message = "The file is not a supported image (JPEG, PNG, GIF, WebP, BMP).";
                return Page();
            }

            stream.Position = 0;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            photo = buffer.ToArray();
        }

        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();

            var (prepared, result) = await ReadCardAsync(photo, rotate180: false, cancellationToken);

            // A card photographed lying sideways can come out upside down; only the model can tell, so flip and ask again.
            if (GetBool(result, "upsideDown"))
                (prepared, result) = await ReadCardAsync(photo, rotate180: true, cancellationToken);

            ElapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 1);
            ModelUsed = SelectedModel;
            CardDetected = prepared.CardDetected;
            PreparedImageDataUri = "data:image/jpeg;base64," + Convert.ToBase64String(prepared.Jpeg);
            ResultJson = JsonSerializer.Serialize(result, Indented);

            if (GetBool(result, "upsideDown"))
            {
                Message = "The card still looks upside down after turning it. Try a photo with the card upright.";
                return Page();
            }

            if (!GetBool(result, "isPokemonCard"))
            {
                Message = "This does not look like a Pokémon card.";
                return Page();
            }

            var finish = GetString(result, "finish")?.ToLowerInvariant() switch
            {
                "normal" => CardFinish.Normal,
                "holofoil" => CardFinish.Holofoil,
                "reverseholofoil" => CardFinish.ReverseHolofoil,
                _ => CardFinish.Unknown,
            };
            DetectedFinish = finish switch
            {
                CardFinish.Normal => "normal",
                CardFinish.Holofoil => "holofoil",
                CardFinish.ReverseHolofoil => "reverse holofoil",
                _ => "unknown holofoil",
            };

            var name = GetString(result, "name");
            var setCode = GetString(result, "setCode")?.ToUpperInvariant() is { } s && Regex.IsMatch(s, "^[A-Z0-9]{2,5}$") ? s : null;
            var languageCode = GetString(result, "languageCode")?.ToUpperInvariant() is { } l && LanguageCodes.Contains(l) ? l : null;
            var cardNumber = NormalizeNumber(GetString(result, "cardNumber"));
            var localId = cardNumber?.Split('/')[0] ?? GetString(result, "localId");
            if (name is null || localId is null)
            {
                Message = "This looks like a Pokémon card, but the name or card number could not be read.";
                return Page();
            }

            // The total after the slash (the 182 in 001/182) identifies the set, so it cross-checks the set code.
            var total = int.TryParse(cardNumber?.Split('/').ElementAtOrDefault(1), out var t) ? t : (int?)null;

            Card = await cards.GetCardByNameAndIdAsync(name, localId, setCode, languageCode, total);
            UsdToEur = await rates.GetUsdToEurAsync(cancellationToken);
            AveragePrice = PriceCalculator.Calculate(Card?.Pricing, finish, UsdToEur);
            AveragePrices = PriceCalculator.CalculateAll(Card?.Pricing, UsdToEur);
            Message = Card is null
                ? $"This looks like a Pokémon card, but \"{name}\" #{localId} was not found in TCGdex."
                : $"Found {Card.Name} ({Card.SetName} #{Card.LocalId}).";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or OpenCvSharp.OpenCVException)
        {
            Message = $"Could not analyze or look up the card with {SelectedModel}: {ex.Message}";
        }

        return Page();
    }

    private async Task<(PreparedCardImage Prepared, JsonElement Result)> ReadCardAsync(byte[] photo, bool rotate180, CancellationToken cancellationToken)
    {
        var prepared = CardImagePreprocessor.PrepareForReading(photo, rotate180);
        using var image = new MemoryStream(prepared.Jpeg);
        var result = await llm.AskAboutImageAsync(image, CardPrompt, SelectedModel!, cancellationToken);
        return (prepared, result);
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

    // Checks the file's leading bytes (magic numbers) rather than trusting the
    // file name or the client-supplied Content-Type.
    private static async Task<string?> DetectImageFormatAsync(Stream stream)
    {
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        var h = header.AsSpan(0, read);

        if (h.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "JPEG";
        if (h.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "PNG";
        if (h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8)) return "GIF";
        if (h.StartsWith("BM"u8)) return "BMP";
        if (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8)) return "WebP";

        return null;
    }
}
