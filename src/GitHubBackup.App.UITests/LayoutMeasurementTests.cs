using System.Drawing;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Checks the layout measurements on synthetic images whose text, frames and lines lie at known pixels.
/// </summary>
public sealed class LayoutMeasurementTests
{
    private const int _white = unchecked((int)0xFFFFFFFF);
    private const int _black = unchecked((int)0xFF000000);
    private const int _gray = unchecked((int)0xFF8C8C8C);
    private const int _faint = unchecked((int)0xFFF0F0F0);
    private const int _cardFill = unchecked((int)0xFFFAFAFA);
    private const int _windowBackground = unchecked((int)0xFFF3F3F3);

    [Fact]
    public void AlignmentSpread_TextOnOneLine_ReturnsZero()
    {
        var image = new Canvas(200, 30, _white).Text(10, 8, 40).Text(110, 8, 40).ToImage();

        var spread = LayoutMeasurement.AlignmentSpread(image, [new(0, 0, 100, 30), new(100, 0, 100, 30)], scale: 1);

        spread.ShouldBe(0);
    }

    [Fact]
    public void AlignmentSpread_CellTextShiftedUp_ReturnsShift()
    {
        var image = new Canvas(200, 30, _white).Text(10, 8, 40).Text(110, 2, 40).ToImage();

        var spread = LayoutMeasurement.AlignmentSpread(image, [new(0, 0, 100, 30), new(100, 0, 100, 30)], scale: 1);

        spread.ShouldBe(6);
    }

    [Fact]
    public void AlignmentSpread_CellWithTwoLines_LeavesItOut()
    {
        var image = new Canvas(300, 40, _white).Text(10, 14, 40).Text(110, 4, 40).Text(110, 22, 40).Text(210, 14, 40).ToImage();

        var spread = LayoutMeasurement.AlignmentSpread(
            image, [new(0, 0, 100, 40), new(100, 0, 100, 40), new(200, 0, 100, 40)], scale: 1);

        spread.ShouldBe(0);
    }

    [Fact]
    public void TextBounds_TextInsideControlFrame_ReturnsOnlyTheText()
    {
        var image = new Canvas(120, 34, _white).Frame(new(2, 2, 116, 30), _gray).Text(12, 12, 30).ToImage();

        var text = LayoutMeasurement.TextBounds(image, new(0, 0, 120, 34), scale: 1);

        text.ShouldBe(new Rectangle(12, 12, 28, 10));
    }

    [Fact]
    public void TextBounds_FullWidthSeparatorLine_ReturnsNull()
    {
        var image = new Canvas(120, 20, _white).Fill(new(0, 10, 120, 1), _gray).ToImage();

        LayoutMeasurement.TextBounds(image, new(0, 0, 120, 20), scale: 1).ShouldBeNull();
    }

    [Fact]
    public void TextBounds_ShortFragmentOfFaintFrame_ReturnsNull()
    {
        var image = new Canvas(120, 30, _white).Fill(new(20, 28, 30, 1), _gray).ToImage();

        LayoutMeasurement.TextBounds(image, new(0, 0, 120, 30), scale: 1).ShouldBeNull();
    }

    [Fact]
    public void VerticalClearance_TextInsideRow_ReturnsSmallerGap()
    {
        var image = new Canvas(120, 24, _white).Text(10, 6, 40).ToImage();

        LayoutMeasurement.VerticalClearance(image, new(0, 0, 120, 24), scale: 1).ShouldBe(6);
    }

    [Fact]
    public void VerticalClearance_RowCutThroughText_ReturnsZero()
    {
        var image = new Canvas(120, 30, _white).Text(10, 16, 40).ToImage();

        LayoutMeasurement.VerticalClearance(image, new(0, 0, 120, 22), scale: 1).ShouldBe(0);
    }

    [Fact]
    public void BorderContrast_VisibleBorder_ReturnsLuminanceDifference()
    {
        var image = Card(_gray);

        var contrast = LayoutMeasurement.BorderContrast(image, new(10, 10, 100, 60), scale: 1);

        contrast.ShouldBe(PixelImage.Luminance(_windowBackground) - PixelImage.Luminance(_gray), 0.5);
    }

    [Fact]
    public void BorderContrast_BorderBetweenTwoPixels_CountsBothHalves()
    {
        var half = unchecked((int)0xFFC0C0C0);
        var image = new Canvas(120, 80, _windowBackground)
            .Fill(new(10, 10, 100, 60), _cardFill)
            .Frame(new(10, 10, 100, 60), _gray)
            .Fill(new(11, 68, 98, 1), half)
            .Fill(new(10, 69, 100, 1), half)
            .ToImage();

        var contrast = LayoutMeasurement.BorderContrast(image, new(10, 10, 100, 60), scale: 1);

        contrast.ShouldBeGreaterThan(ScreenshotAssert.MinBorderContrast * 2);
    }

    [Fact]
    public void BorderContrast_FaintBorder_FallsBelowLimit()
    {
        var image = Card(_faint);

        var contrast = LayoutMeasurement.BorderContrast(image, new(10, 10, 100, 60), scale: 1);

        contrast.ShouldBeLessThan(ScreenshotAssert.MinBorderContrast);
    }

    [Fact]
    public void LineContrast_SeparatorLine_ReturnsLuminanceDifference()
    {
        var image = new Canvas(120, 20, _white).Fill(new(0, 10, 120, 1), _gray).ToImage();

        var contrast = LayoutMeasurement.LineContrast(image, new(5, 10, 110, 1), scale: 1);

        contrast.ShouldBe(PixelImage.Luminance(_white) - PixelImage.Luminance(_gray), 0.5);
    }

    [Fact]
    public void TextContrast_BlackTextOnWhite_ReturnsFullDifference()
    {
        var image = new Canvas(120, 24, _white).Text(10, 6, 40).ToImage();

        LayoutMeasurement.TextContrast(image, new(0, 0, 120, 24)).ShouldBe(255, 0.5);
    }

    [Fact]
    public void EnsureNotBlank_OneColorCapture_ThrowsWithLockedDesktopHint()
    {
        var image = new Canvas(200, 100, _white).ToImage();

        var exception = Should.Throw<InvalidOperationException>(() => LayoutMeasurement.EnsureNotBlank(image, "the main window"));

        exception.Message.ShouldContain("the main window is blank");
        exception.Message.ShouldContain("locked");
    }

    [Fact]
    public void EnsureNotBlank_CaptureWithText_DoesNotThrow()
    {
        var image = new Canvas(200, 100, _white).Text(10, 10, 120).Text(10, 40, 120).ToImage();

        Should.NotThrow(() => LayoutMeasurement.EnsureNotBlank(image, "the main window"));
    }

    /// <summary>
    /// Draws a card with a fill and a border of <paramref name="border"/> on the window background.
    /// </summary>
    /// <param name="border">The ARGB value of the border.</param>
    /// <returns>The image; the card covers 10, 10, 100 by 60.</returns>
    private static PixelImage Card(int border) =>
        new Canvas(120, 80, _windowBackground)
            .Fill(new(10, 10, 100, 60), _cardFill)
            .Frame(new(10, 10, 100, 60), border)
            .ToImage();

    /// <summary>
    /// Draws rectangles, frames and text-like glyphs on a one-color background.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="background">The ARGB value of the background.</param>
    private sealed class Canvas(int width, int height, int background)
    {
        private readonly int[] _pixels = [.. Enumerable.Repeat(background, width * height)];

        /// <summary>
        /// Fills a rectangle.
        /// </summary>
        /// <param name="rectangle">The rectangle.</param>
        /// <param name="argb">The ARGB value.</param>
        /// <returns>This canvas.</returns>
        public Canvas Fill(Rectangle rectangle, int argb)
        {
            for (var y = rectangle.Top; y < rectangle.Bottom; y++)
            {
                for (var x = rectangle.Left; x < rectangle.Right; x++)
                {
                    _pixels[(y * width) + x] = argb;
                }
            }

            return this;
        }

        /// <summary>
        /// Draws the 1-pixel outline of a rectangle, as the frame of a card or a control.
        /// </summary>
        /// <param name="rectangle">The rectangle.</param>
        /// <param name="argb">The ARGB value.</param>
        /// <returns>This canvas.</returns>
        public Canvas Frame(Rectangle rectangle, int argb) =>
            Fill(new(rectangle.Left, rectangle.Top, rectangle.Width, 1), argb)
                .Fill(new(rectangle.Left, rectangle.Bottom - 1, rectangle.Width, 1), argb)
                .Fill(new(rectangle.Left, rectangle.Top, 1, rectangle.Height), argb)
                .Fill(new(rectangle.Right - 1, rectangle.Top, 1, rectangle.Height), argb);

        /// <summary>
        /// Draws a word of black glyphs 10 pixels high: blocks 4 pixels wide with 2-pixel gaps, as text leaves gaps
        /// between letters.
        /// </summary>
        /// <param name="left">The left edge of the word.</param>
        /// <param name="top">The top edge of the glyphs.</param>
        /// <param name="length">The width of the word in pixels.</param>
        /// <returns>This canvas.</returns>
        public Canvas Text(int left, int top, int length)
        {
            for (var x = left; x < left + length; x += 6)
            {
                Fill(new(x, top, Math.Min(4, left + length - x), 10), _black);
            }

            return this;
        }

        /// <summary>
        /// Returns the drawn image.
        /// </summary>
        /// <returns>The image.</returns>
        public PixelImage ToImage() => new(width, height, [.. _pixels]);
    }
}
