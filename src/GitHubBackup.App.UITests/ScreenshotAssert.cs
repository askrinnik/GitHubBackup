using System.Drawing;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Asserts the visual design rules of the windows on a screenshot: one baseline per table row, no text that touches
/// or crosses an edge, borders and separators that stand out in both themes.
/// </summary>
/// <remarks>
/// The limits are calibrated on renders of the windows in the light and the dark theme at 96 DPI: each contrast limit
/// is half of the lowest value measured there, so a border drawn half as strongly still passes while a faint divider
/// brush fails.
/// </remarks>
internal static class ScreenshotAssert
{
    /// <summary>
    /// The lowest contrast of a card border. The lowest value measured is 57, in the light theme, where the layout
    /// places the bottom border between two device pixels; the dark theme measures from 127 up.
    /// </summary>
    public const double MinBorderContrast = 28;

    /// <summary>
    /// The lowest contrast of the line between the header and the table of the results panel. The values measured
    /// are 74 in the light theme and 137 in the dark theme.
    /// </summary>
    public const double MinLineContrast = 37;

    /// <summary>
    /// The lowest contrast of the text of a selected table row against the selection. The lowest value measured is
    /// 82, in the dark theme, for the text of the combo box and the text boxes of a new repository.
    /// </summary>
    public const double MinTextContrast = 41;

    /// <summary>
    /// The space, in pixels, that text keeps from the top and the bottom of its cell; the lowest value measured is one
    /// pixel, for the two folder lines of a renamed repository, and text that an edge cuts measures zero.
    /// </summary>
    public const int MinClearance = 1;

    /// <summary>
    /// The difference, in device-independent pixels, that the text tops of one row may have: the tops of capitals and
    /// of ascenders differ by up to one pixel, while text placed at the top of a cell next to a centred control lies
    /// six pixels or more above it.
    /// </summary>
    private const double _alignmentTolerance = 2;

    /// <summary>
    /// Asserts that the text of the cells of one table row starts on one pixel row.
    /// </summary>
    /// <param name="image">The screenshot.</param>
    /// <param name="table">The geometry of the table in the screenshot.</param>
    /// <param name="row">The index of the row.</param>
    /// <param name="scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <param name="description">The window and the state, for the message.</param>
    public static void RowAligned(PixelImage image, TableGeometry table, int row, double scale, string description) =>
        LayoutMeasurement.AlignmentSpread(image, table.Cells(row), scale)
            .ShouldBeLessThanOrEqualTo(
                (int)Math.Ceiling(_alignmentTolerance * scale),
                $"{description}: the text tops of row {row} at {table.Rows[row]} differ");

    /// <summary>
    /// Asserts that the text of every cell of one table row keeps clear of the top and the bottom of its cell.
    /// </summary>
    /// <param name="image">The screenshot.</param>
    /// <param name="table">The geometry of the table in the screenshot.</param>
    /// <param name="row">The index of the row.</param>
    /// <param name="scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <param name="description">The window and the state, for the message.</param>
    public static void RowClear(PixelImage image, TableGeometry table, int row, double scale, string description)
    {
        foreach (var cell in table.Cells(row))
        {
            TextClear(image, cell, scale, $"{description}: row {row}, cell {cell}", requireText: false);
        }
    }

    /// <summary>
    /// Asserts that the text of a region keeps clear of its top and bottom edge.
    /// </summary>
    /// <param name="image">The screenshot.</param>
    /// <param name="region">The region, such as a cell or the inside of a card.</param>
    /// <param name="scale">The display scale of the window, 1 at 96 DPI.</param>
    /// <param name="description">The region, for the message.</param>
    /// <param name="requireText">Whether the region must hold text; an empty cell passes otherwise.</param>
    public static void TextClear(PixelImage image, Rectangle region, double scale, string description, bool requireText)
    {
        var clearance = LayoutMeasurement.VerticalClearance(image, region, scale);
        if (clearance < 0 && !requireText)
        {
            return;
        }

        clearance.ShouldBeGreaterThanOrEqualTo(
            MinClearance, $"{description}: the text touches or crosses an edge, or there is no text (-1)");
    }
}
