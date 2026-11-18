using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Takes screenshots of a window of the application from the screen for the layout measurements.
/// </summary>
internal static class ScreenCapture
{
    /// <summary>The most captures taken while waiting for the window to stop changing.</summary>
    private const int _maxAttempts = 20;

    /// <summary>The time between two captures while waiting for the window to stop changing.</summary>
    private static readonly TimeSpan _settleInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Captures a window once two captures in a row are equal, so that a layout pass or an animation in progress does
    /// not land in the measurements.
    /// </summary>
    /// <remarks>
    /// The capture copies the screen inside the bounds of the window, so the window must be on top and not covered.
    /// </remarks>
    /// <param name="window">The window.</param>
    /// <param name="description">What the window shows, for the message of a blank capture.</param>
    /// <returns>The capture; its top left corner is the top left corner of the bounds of the window.</returns>
    /// <exception cref="InvalidOperationException">The capture is blank, as on a locked desktop.</exception>
    public static PixelImage Window(AutomationElement window, string description)
    {
        var previous = Take(window);
        for (var attempt = 1; attempt < _maxAttempts; attempt++)
        {
            Thread.Sleep(_settleInterval);
            var current = Take(window);
            if (SamePixels(previous, current))
            {
                break;
            }

            previous = current;
        }

        LayoutMeasurement.EnsureNotBlank(previous, description);
        return previous;
    }

    /// <summary>
    /// Returns the bounds of an element in the coordinates of a capture of <paramref name="window"/>.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="window">The captured window.</param>
    /// <returns>The bounds in the capture.</returns>
    public static Rectangle Bounds(AutomationElement element, AutomationElement window) =>
        TableGeometry.ToImage(element.BoundingRectangle, window.BoundingRectangle.Location);

    /// <summary>
    /// Captures the screen inside the bounds of an element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The capture.</returns>
    private static PixelImage Take(AutomationElement element)
    {
        using var image = Capture.Element(element);
        return PixelImage.FromBitmap(image.Bitmap);
    }

    /// <summary>
    /// Returns whether two captures have the same size and the same pixels.
    /// </summary>
    /// <param name="first">The first capture.</param>
    /// <param name="second">The second capture.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    private static bool SamePixels(PixelImage first, PixelImage second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return false;
        }

        for (var y = 0; y < first.Height; y++)
        {
            for (var x = 0; x < first.Width; x++)
            {
                if (first[x, y] != second[x, y])
                {
                    return false;
                }
            }
        }

        return true;
    }
}
