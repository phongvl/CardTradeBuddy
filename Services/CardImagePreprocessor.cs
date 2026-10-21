using OpenCvSharp;

namespace CardTradeBuddy.Services;

/// <param name="Jpeg">The image to send to the vision model.</param>
/// <param name="CardDetected">False when no card outline was found and the whole photo was used instead.</param>
public record PreparedCardImage(byte[] Jpeg, bool CardDetected);

/// <summary>
/// Turns a photo of a card into one compact image for the vision model:
/// <list type="number">
/// <item>finds the card's outline in the photo and straightens it (perspective correction),</item>
/// <item>cuts out the top strip (name, HP, type) and the bottom strip (set code, language, card number), enlarged,</item>
/// <item>adds a small thumbnail of the artwork so foil can still be judged,</item>
/// <item>stacks them into a single image, leaving out the rest of the card (attacks and text).</item>
/// </list>
/// The model then reads small print at a readable size from far fewer pixels than the original photo.
/// </summary>
public static class CardImagePreprocessor
{
    // PokÃ©mon cards are 63 x 88 mm.
    private const double CardAspect = 63.0 / 88.0;

    // Detection works on a reduced copy of the photo (faster, and less sensitive to noise).
    private const int DetectionMaxSide = 900;

    // The card must fill at least this share of the photo to be accepted as "the card".
    private const double MinCardAreaShare = 0.05;

    // Band of the (upright) card height used for each part.
    private const double TopEnd = 0.13;
    private const double ArtStart = 0.13, ArtEnd = 0.55;
    private const double BottomStart = 0.86;

    private const int PanelWidth = 1280;     // width of the top and bottom strips in the final image
    private const int ThumbnailWidth = 480;  // width of the artwork thumbnail
    private const int GapHeight = 16;
    private const int MaxCardWidth = 1500;   // cap on the straightened card, to bound the work done

    /// <param name="rotate180">Turn the card upside down first. A card that lies sideways can come out either way up,
    /// and only the reader of the result can tell, so callers retry with this set when the model says it is upside down.</param>
    public static PreparedCardImage PrepareForReading(byte[] image, bool rotate180 = false)
    {
        // Decoding applies the photo's EXIF rotation, so a sideways phone photo comes out upright.
        using var photo = Cv2.ImDecode(image, ImreadModes.Color);
        if (photo.Empty())
            throw new InvalidOperationException("The image could not be decoded.");

        using var straightened = ExtractCard(photo);
        var detected = straightened is not null;

        var card = straightened ?? photo;
        Mat? turned = null;
        try
        {
            // No outline found: the whole photo is used as the card. A landscape photo is assumed to be a card lying on its side.
            if (!detected && photo.Width > photo.Height)
            {
                turned = new Mat();
                Cv2.Rotate(photo, turned, RotateFlags.Rotate90Clockwise);
                card = turned;
            }

            if (rotate180)
            {
                var flipped = new Mat();
                Cv2.Rotate(card, flipped, RotateFlags.Rotate180);
                turned?.Dispose();
                turned = flipped;
                card = flipped;
            }

            using var composite = BuildComposite(card);
            Cv2.ImEncode(".jpg", composite, out var jpeg, new ImageEncodingParam(ImwriteFlags.JpegQuality, 92));
            return new PreparedCardImage(jpeg, detected);
        }
        finally
        {
            turned?.Dispose();
        }
    }

    /// <summary>Finds the largest card-shaped quadrilateral and returns it warped to an upright rectangle, or null.</summary>
    private static Mat? ExtractCard(Mat photo)
    {
        var ratio = Math.Min(1.0, DetectionMaxSide / (double)Math.Max(photo.Width, photo.Height));
        using var small = new Mat();
        Cv2.Resize(photo, small, new Size(0, 0), ratio, ratio, InterpolationFlags.Area);

        using var gray = new Mat();
        Cv2.CvtColor(small, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);

        using var edges = new Mat();
        Cv2.Canny(gray, edges, 40, 120);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.Dilate(edges, edges, kernel, iterations: 2); // close small gaps in the outline

        Cv2.FindContours(edges, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var minArea = small.Width * small.Height * MinCardAreaShare;
        Point[]? best = null;
        var bestArea = 0.0;

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < minArea || area <= bestArea)
                continue;

            var approx = Cv2.ApproxPolyDP(contour, 0.02 * Cv2.ArcLength(contour, true), true);
            if (approx.Length != 4 || !Cv2.IsContourConvex(approx))
                continue;

            var quad = Order(approx);
            var width = Math.Max(Distance(quad[0], quad[1]), Distance(quad[3], quad[2]));
            var height = Math.Max(Distance(quad[0], quad[3]), Distance(quad[1], quad[2]));
            var aspect = Math.Min(width, height) / Math.Max(width, height);
            if (Math.Abs(aspect - CardAspect) > 0.12)
                continue;

            best = quad;
            bestArea = area;
        }

        if (best is null)
            return null;

        // Back to full-resolution coordinates.
        var corners = best.Select(p => new Point2f((float)(p.X / ratio), (float)(p.Y / ratio))).ToArray();
        var w = Math.Max(Distance(corners[0], corners[1]), Distance(corners[3], corners[2]));
        var h = Math.Max(Distance(corners[0], corners[3]), Distance(corners[1], corners[2]));

        // A card photographed lying sideways: turn the quad so its short side is on top.
        if (w > h)
        {
            corners = [corners[3], corners[0], corners[1], corners[2]];
            (w, h) = (h, w);
        }

        var outWidth = (int)Math.Clamp(w, 300, MaxCardWidth);
        var outHeight = (int)(outWidth / CardAspect);
        Point2f[] target = [new(0, 0), new(outWidth, 0), new(outWidth, outHeight), new(0, outHeight)];

        using var transform = Cv2.GetPerspectiveTransform(corners, target);
        var card = new Mat();
        Cv2.WarpPerspective(photo, card, transform, new Size(outWidth, outHeight), InterpolationFlags.Cubic);
        return card;
    }

    private static Mat BuildComposite(Mat card)
    {
        using var top = Strip(card, 0, TopEnd, PanelWidth);
        using var bottom = Strip(card, BottomStart, 1, PanelWidth);
        using var art = Strip(card, ArtStart, ArtEnd, ThumbnailWidth);
        using var gap = new Mat(GapHeight, PanelWidth, MatType.CV_8UC3, new Scalar(128, 128, 128));

        // The thumbnail is narrower than the strips, so it sits centred on a grey band of the same width.
        using var artBand = new Mat(art.Height, PanelWidth, MatType.CV_8UC3, new Scalar(128, 128, 128));
        using (var slot = new Mat(artBand, new Rect((PanelWidth - ThumbnailWidth) / 2, 0, ThumbnailWidth, art.Height)))
            art.CopyTo(slot);

        var composite = new Mat();
        Cv2.VConcat(new[] { top, gap, artBand, gap, bottom }, composite);
        return composite;
    }

    // A horizontal band of the card (fractions of its height), resized to the given width.
    private static Mat Strip(Mat card, double from, double to, int width)
    {
        var y = (int)(card.Height * from);
        var h = Math.Max(1, (int)(card.Height * to) - y);
        using var band = new Mat(card, new Rect(0, y, card.Width, h));

        var scale = width / (double)card.Width;
        var resized = new Mat();
        Cv2.Resize(band, resized, new Size(width, Math.Max(1, (int)(h * scale))), 0, 0,
            scale > 1 ? InterpolationFlags.Cubic : InterpolationFlags.Area);
        return resized;
    }

    // Order corners as top-left, top-right, bottom-right, bottom-left.
    private static Point[] Order(Point[] points)
    {
        var bySum = points.OrderBy(p => p.X + p.Y).ToArray();
        var byDiff = points.OrderBy(p => p.Y - p.X).ToArray();
        return [bySum[0], byDiff[0], bySum[^1], byDiff[^1]];
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private static double Distance(Point2f a, Point2f b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}

