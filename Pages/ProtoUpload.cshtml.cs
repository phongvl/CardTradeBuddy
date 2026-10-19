using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardTradeBuddy.Pages;

public class ProtoUploadModel : PageModel
{
    [BindProperty]
    public IFormFile? Upload { get; set; }

    public string? Message { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Upload is null || Upload.Length == 0)
        {
            Message = "Please choose a file.";
            return Page();
        }

        await using var stream = Upload.OpenReadStream();
        var format = await DetectImageFormatAsync(stream);

        Message = format is null
            ? "The file is not a supported image (JPEG, PNG, GIF, WebP, BMP)."
            : $"The file is a {format} image.";

        return Page();
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
