using System.Drawing;
using System.Globalization;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Measures the layout of a screenshot: the background of a region, the bounds of its text without the lines of
/// borders and controls, how well text lines up across a table row, and how strongly a border or a line stands out.
/// </summary>
/// <remarks>
/// Every length is in pixels of the screenshot; a caller scales its tolerances by the display scale of the window.
/// </remarks>
internal static class LayoutMeasurement
{
    /// <summary>
    /// The smallest color distance, 0 to 255, at which a pixel counts as ink rather than background. Text in the
    /// secondary text color of both themes stands far above it; grid lines and the fills of controls stay below.
    /// </summary>
    public const int InkThreshold = 60;

    /// <summary>
    /// The share of the pixels of a capture in one single color at and above which the capture counts as blank: a
    /// rendered window always has text, borders and controls in other colors.
    /// </summary>
    public const double BlankColorShare = 0.995;

    /// <summary>
    /// The share of the width or the height of a region that an unbroken run of ink must cover to count as a line of a
    /// border or a control rather than text; glyphs leave gaps far shorter than that.
    /// </summary>
    private const double _lineRunShare = 0.6;

    /// <summary>
    /// The factor by which the ink of a cell may be higher than the median ink of its row before the cell counts as
    /// holding more than one line of text.
    /// </summary>
    private const double _multiLineFactor = 1.6;

    /// <summary>The corner radius, in device-independent pixels, of the cards and of the Fluent controls.</summary>
    private const double _cornerRadius = 4;

    /// <summary>
    /// The height, in device-independent pixels, of the lowest band of ink that counts as text: lower than the
    /// x-height of the body font, higher than a line of a frame.
    /// </summary>
    private const double _minGlyphHeight = 4;

    /// <summary>
    /// Throws when a capture is blank, as a capture of a locked, minimized or disconnected session is.
    /// </summary>
    /// <param name="image">The capture.</param>
    /// <param name="description">What was captured, for the message.</param>
    /// <exception cref="InvalidOperationException">The capture is blank.</exception>
    public static void EnsureNotBlank(PixelImage image, string description)
    {
        var total = image.Width * image.Height;
        var share = total == 0 ? 1 : (double)CountOf(image, image.Bounds, Background(image, image.Bounds)) / total;
        if (share >= BlankColorShare)
        {
            throw new InvalidOperationException(
                $"The capture of {description} is blank ({share.ToString("P1", CultureInfo.InvariantCulture)} of its pixels have one color). Windows does not "
                + "render windows while the workstation is locked, the session is minimized or disconnected, or the "
                + "screen saver runs; run the screenshot tests on an unlocked interactive desktop.");
        }
    }

    /// <summary>
    /// Returns the most frequent color of a region, its background.
    /// </summary>
    /// <param name="image">The screenshot.</param>
    /// <param name="region">The region, inside the image.</param>
    /// <returns>The ARGB value.</returns>
    public static int Background(PixelImage image, Rectangle region)
    {
        Dictionary<int, int> counts = [];
        var best = 0;
        var bestCount = -1;
        for (var y = region.Top; y < region.Bottom; y++)
        {
            for (var x = region.Left; x < region.Right; x++)
            {
                var pixel = image[x, y];
                var count = counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
                if (count > bestCount)
                {
                    best = pixel;
                    bestCount = count;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the bounds of the text in a region, without the lines of borders, grid lines, separators and the frames
    /// of controls such as a combo box or a text box.
    /// </summary>
    /// <remarks>
    /// A line is an unbroken run of ink across most of the width or the height of the region. The rounded corners at
    /// the ends of a line are not part of the run, so ink close to a line and to the edge of the region or another
    /// line is left out too. What remains counts as text only in bands of rows at least as high as the smallest
    /// glyph, which drops the short fragments of a frame that are too faint to form a whole line.
    /// </remarks>
    /// <param name="image">The screenshot.</param>
    /// <param name="region">The region, inside the image.</param>
    /// <param name="scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <returns>The bounds of the text, or <see langword="null"/> when the region holds none.</returns>
    public static Rectangle? TextBounds(PixelImage image, Rectangle region, double scale)
    {
        var ink = InkMask(image, region, Background(image, region));
        var width = region.Width;
        var height = region.Height;
        var corner = (int)Math.Ceiling(_cornerRadius * scale) + 1;
        var lineRows = Enumerable.Range(0, height)
            .Where(y => LongestRun(x => ink[y, x], width) >= _lineRunShare * width)
            .ToHashSet();
        var lineColumns = Enumerable.Range(0, width)
            .Where(x => LongestRun(y => ink[y, x], height) >= _lineRunShare * height)
            .ToHashSet();
        bool IsCorner(int x, int y) =>
            (IsNear(lineRows, y, corner) && (x < corner || x >= width - corner || IsNear(lineColumns, x, corner)))
            || (IsNear(lineColumns, x, corner) && (y < corner || y >= height - corner || IsNear(lineRows, y, corner)));

        var rowLeft = new int?[height];
        var rowRight = new int?[height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (ink[y, x] && !lineRows.Contains(y) && !lineColumns.Contains(x) && !IsCorner(x, y))
                {
                    rowLeft[y] ??= x;
                    rowRight[y] = x;
                }
            }
        }

        var minBand = (int)Math.Ceiling(_minGlyphHeight * scale);
        int? left = null, top = null, right = null, bottom = null;
        for (var start = 0; start < height;)
        {
            if (rowLeft[start] is null)
            {
                start++;
                continue;
            }

            var end = start;
            while (end + 1 < height && rowLeft[end + 1] is not null)
            {
                end++;
            }

            if (end - start + 1 >= minBand)
            {
                for (var y = start; y <= end; y++)
                {
                    left = Math.Min(left ?? rowLeft[y]!.Value, rowLeft[y]!.Value);
                    right = Math.Max(right ?? rowRight[y]!.Value, rowRight[y]!.Value);
                }

                top ??= start;
                bottom = end;
            }

            start = end + 1;
        }

        return top is null
            ? null
            : Rectangle.FromLTRB(region.Left + left!.Value, region.Top + top.Value, region.Left + right!.Value + 1, region.Top + bottom!.Value + 1);
    }

    /// <summary>
    /// Returns how far apart the tops of the text of the cells of one table row are.
    /// </summary>
    /// <remarks>
    /// Empty cells are left out, and so is a cell that holds more than one line of text, such as a stack of two
    /// folder names: centred in the row, its first line starts above the single lines on purpose.
    /// </remarks>
    /// <param name="image">The screenshot.</param>
    /// <param name="cells">The cells of the row.</param>
    /// <param name="scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <returns>The difference in pixels between the highest and the lowest text top, or 0 for fewer than two cells.</returns>
    public static int AlignmentSpread(PixelImage image, IReadOnlyList<Rectangle> cells, double scale)
    {
        var texts = cells
            .Select(cell => TextBounds(image, cell, scale))
            .OfType<Rectangle>()
            .ToList();
        if (texts.Count < 2)
        {
            return 0;
        }

        var medianHeight = texts.Select(text => text.Height).Order().ElementAt((texts.Count - 1) / 2);
        var tops = texts.Where(text => text.Height <= _multiLineFactor * medianHeight).Select(text => text.Top).ToList();
        return tops.Count < 2 ? 0 : tops.Max() - tops.Min();
    }

    /// <summary>
    /// Returns the smallest gap between the text of a region and the edges of the region, top and bottom; a negative
    /// gap means the region holds no text.
    /// </summary>
    /// <remarks>
    /// Text that the edge of a scrolling area or of a border cuts reaches the edge of its region, so the gap is zero.
    /// </remarks>
    /// <param name="image">The screenshot.</param>
    /// <param name="region">The region, such as a cell or the inside of a card.</param>
    /// <param name="scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <returns>The gap in pixels, or -1 when the region holds no text.</returns>
    public static int VerticalClearance(PixelImage image, Rectangle region, double scale) =>
        TextBounds(image, region, scale) is { } text
            ? Math.Min(text.Top - region.Top, region.Bottom - text.Bottom)
            : -1;

    /// <summary>
    /// Returns how strongly the border at the edges of an element, such as a card, stands out from the fill inside it
    /// and from the background outside it.
    /// </summary>
    /// <remarks>
    /// Each side is sampled along its length, away from the rounded corners. At each sample every pixel near the edge
    /// adds the smaller of its luminance differences to the two backgrounds, so that a 1-pixel border that the layout
    /// places between two device pixels and draws at half strength on both counts in full. A side counts with the
    /// median of its samples, the element with its weakest side.
    /// </remarks>
    /// <param name="image">The screenshot.</param>
    /// <param name="element">The bounds of the element, with the border at its outermost pixels.</param>
    /// <param name="scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <returns>The contrast as a sum of luminance differences across the edge.</returns>
    public static double BorderContrast(PixelImage image, Rectangle element, double scale)
    {
        var band = (int)Math.Ceiling(scale) + 1;
        var gap = band + 2;
        var corner = (int)Math.Ceiling(8 * scale);
        var sides = new[]
        {
            Samples(element.Left + corner, element.Right - corner, x => Profile(image, x, element.Top, 0, 1, band, gap)),
            Samples(element.Left + corner, element.Right - corner, x => Profile(image, x, element.Bottom - 1, 0, -1, band, gap)),
            Samples(element.Top + corner, element.Bottom - corner, y => Profile(image, element.Left, y, 1, 0, band, gap)),
            Samples(element.Top + corner, element.Bottom - corner, y => Profile(image, element.Right - 1, y, -1, 0, band, gap)),
        };
        return sides.Min(Median);
    }

    /// <summary>
    /// Returns how strongly a horizontal line, such as a separator, stands out from the background above and below it.
    /// </summary>
    /// <remarks>
    /// The contrast adds up across the rows of the line, as in <see cref="BorderContrast"/>.
    /// </remarks>
    /// <param name="image">The screenshot.</param>
    /// <param name="line">The bounds of the line.</param>
    /// <param name="scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <returns>The median contrast along the line as a sum of luminance differences across it.</returns>
    public static double LineContrast(PixelImage image, Rectangle line, double scale)
    {
        var gap = (int)Math.Ceiling(3 * scale);
        var samples = Samples(line.Left + gap, line.Right - gap, x =>
        {
            var above = LuminanceAt(image, x, line.Top - gap);
            var below = LuminanceAt(image, x, line.Bottom - 1 + gap);
            return Enumerable.Range(line.Top - 1, line.Height + 2)
                .Select(y => LuminanceAt(image, x, y))
                .Sum(value => Math.Min(Math.Abs(value - above), Math.Abs(value - below)));
        });
        return Median(samples);
    }

    /// <summary>
    /// Returns how strongly the text of a region stands out from its background.
    /// </summary>
    /// <param name="image">The screenshot.</param>
    /// <param name="region">The region, such as a cell of a selected row.</param>
    /// <returns>
    /// The 90th percentile of the luminance difference between the ink and the background, 0 to 255, or 0 when the
    /// region holds no ink.
    /// </returns>
    public static double TextContrast(PixelImage image, Rectangle region)
    {
        var background = Background(image, region);
        var backgroundLuminance = PixelImage.Luminance(background);
        var ink = InkMask(image, region, background);
        var differences = new List<double>();
        for (var y = 0; y < region.Height; y++)
        {
            for (var x = 0; x < region.Width; x++)
            {
                if (ink[y, x])
                {
                    differences.Add(Math.Abs(image.Luminance(region.Left + x, region.Top + y) - backgroundLuminance));
                }
            }
        }

        return differences.Count == 0 ? 0 : differences.Order().ElementAt((int)(differences.Count * 0.9));
    }

    /// <summary>
    /// Marks the pixels of a region that differ from its background by at least <see cref="InkThreshold"/>.
    /// </summary>
    /// <param name="image">The screenshot.</param>
    /// <param name="region">The region.</param>
    /// <param name="background">The ARGB value of the background.</param>
    /// <returns>The mask, indexed by row and column relative to the region.</returns>
    private static bool[,] InkMask(PixelImage image, Rectangle region, int background)
    {
        var ink = new bool[region.Height, region.Width];
        for (var y = 0; y < region.Height; y++)
        {
            for (var x = 0; x < region.Width; x++)
            {
                ink[y, x] = PixelImage.Distance(image[region.Left + x, region.Top + y], background) >= InkThreshold;
            }
        }

        return ink;
    }

    /// <summary>
    /// Counts the pixels of a region with one color.
    /// </summary>
    /// <param name="image">The screenshot.</param>
    /// <param name="region">The region.</param>
    /// <param name="argb">The ARGB value.</param>
    /// <returns>The number of pixels.</returns>
    private static int CountOf(PixelImage image, Rectangle region, int argb)
    {
        var count = 0;
        for (var y = region.Top; y < region.Bottom; y++)
        {
            for (var x = region.Left; x < region.Right; x++)
            {
                if (image[x, y] == argb)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the length of the longest unbroken run of ink along one row or column.
    /// </summary>
    /// <param name="isInk">Tells whether the pixel at a position is ink.</param>
    /// <param name="length">The number of positions.</param>
    /// <returns>The length of the run in pixels.</returns>
    private static int LongestRun(Func<int, bool> isInk, int length)
    {
        var longest = 0;
        var current = 0;
        for (var index = 0; index < length; index++)
        {
            current = isInk(index) ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    /// <summary>
    /// Returns whether a position lies within <paramref name="distance"/> of one of <paramref name="lines"/>.
    /// </summary>
    /// <param name="lines">The positions of the lines.</param>
    /// <param name="position">The position.</param>
    /// <param name="distance">The distance in pixels.</param>
    /// <returns><see langword="true"/> when a line is that close.</returns>
    private static bool IsNear(HashSet<int> lines, int position, int distance) =>
        lines.Count > 0 && Enumerable.Range(position - distance, (2 * distance) + 1).Any(lines.Contains);

    /// <summary>
    /// Measures a sample every few pixels between two positions.
    /// </summary>
    /// <param name="from">The first position.</param>
    /// <param name="to">The position after the last.</param>
    /// <param name="measure">Measures at one position.</param>
    /// <returns>The measurements, or a single 0 when the range is empty.</returns>
    private static List<double> Samples(int from, int to, Func<int, double> measure)
    {
        const int step = 3;
        var samples = new List<double>();
        for (var position = from; position < to; position += step)
        {
            samples.Add(measure(position));
        }

        return samples.Count == 0 ? [0] : samples;
    }

    /// <summary>
    /// Measures the contrast of a border across one edge at one position.
    /// </summary>
    /// <param name="image">The screenshot.</param>
    /// <param name="x">The column of the outermost pixel of the edge.</param>
    /// <param name="y">The row of the outermost pixel of the edge.</param>
    /// <param name="dx">The column step towards the inside of the element.</param>
    /// <param name="dy">The row step towards the inside of the element.</param>
    /// <param name="band">The number of pixels from the edge inwards that the border may cover.</param>
    /// <param name="gap">The distance from the edge at which the backgrounds are read.</param>
    /// <returns>The contrast as a sum of luminance differences across the edge.</returns>
    private static double Profile(PixelImage image, int x, int y, int dx, int dy, int band, int gap)
    {
        var inside = LuminanceAt(image, x + (dx * gap), y + (dy * gap));
        var outside = LuminanceAt(image, x - (dx * gap), y - (dy * gap));
        return Enumerable.Range(-1, band + 1)
            .Select(step => LuminanceAt(image, x + (dx * step), y + (dy * step)))
            .Sum(value => Math.Min(Math.Abs(value - inside), Math.Abs(value - outside)));
    }

    /// <summary>
    /// Returns the luminance of the pixel nearest to a position, so that a sample next to the edge of the image reads
    /// the edge instead of failing.
    /// </summary>
    /// <param name="image">The screenshot.</param>
    /// <param name="x">The column, possibly outside the image.</param>
    /// <param name="y">The row, possibly outside the image.</param>
    /// <returns>The luminance from 0 to 255.</returns>
    private static double LuminanceAt(PixelImage image, int x, int y) =>
        image.Luminance(Math.Clamp(x, 0, image.Width - 1), Math.Clamp(y, 0, image.Height - 1));

    /// <summary>
    /// Returns the median of the values.
    /// </summary>
    /// <param name="values">The values, at least one.</param>
    /// <returns>The median.</returns>
    private static double Median(IReadOnlyCollection<double> values) => values.Order().ElementAt(values.Count / 2);
}
