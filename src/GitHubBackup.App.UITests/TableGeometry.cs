using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Holds where the visible columns and rows of a table lie in a screenshot, so that a measurement can look at one
/// cell at a time.
/// </summary>
/// <param name="Bounds">The visible part of the table.</param>
/// <param name="Columns">The left and right edge of every visible column, left to right.</param>
/// <param name="Rows">The visible part of every row whose top lies inside the table, top to bottom.</param>
internal sealed record TableGeometry(Rectangle Bounds, IReadOnlyList<(int Left, int Right)> Columns, IReadOnlyList<Rectangle> Rows)
{
    /// <summary>
    /// The narrowest column, in pixels, that a cell is measured in; a column scrolled almost out of view holds only
    /// part of a glyph.
    /// </summary>
    private const int _minColumnWidth = 12;

    /// <summary>
    /// The pixels left out at the left and right edge of every cell, where the grid lines and the column separators
    /// of the header lie.
    /// </summary>
    private const int _cellInset = 2;

    /// <summary>
    /// Returns the cells of one row, left to right, each cut to the visible part of the table.
    /// </summary>
    /// <param name="row">The index of the row.</param>
    /// <returns>The cell rectangles.</returns>
    public IReadOnlyList<Rectangle> Cells(int row) =>
        [.. Columns
            .Select(column => Rectangle.Intersect(
                Rectangle.FromLTRB(column.Left + _cellInset, Rows[row].Top, column.Right - _cellInset, Rows[row].Bottom),
                Bounds))
            .Where(cell => cell.Width >= _minColumnWidth && cell.Height > 0)];

    /// <summary>
    /// Returns the cells of the column header, left to right: the band between the top of the table and its first
    /// row, or the whole table when no row is visible.
    /// </summary>
    /// <returns>The cell rectangles.</returns>
    public IReadOnlyList<Rectangle> HeaderCells()
    {
        var bottom = Rows.Count > 0 ? Rows[0].Top : Bounds.Bottom;
        return [.. Columns
            .Select(column => Rectangle.Intersect(
                Rectangle.FromLTRB(column.Left + _cellInset, Bounds.Top, column.Right - _cellInset, bottom),
                Bounds))
            .Where(cell => cell.Width >= _minColumnWidth && cell.Height > 0)];
    }

    /// <summary>
    /// Returns the indexes of the rows that are visible at their full height: every row except one that reaches the
    /// bottom edge of the table and is lower than the tallest row, which the edge of a scrolling table cuts on purpose.
    /// </summary>
    /// <returns>The row indexes, top to bottom.</returns>
    public IReadOnlyList<int> WholeRows()
    {
        if (Rows.Count == 0)
        {
            return [];
        }

        var tallest = Rows.Max(row => row.Height);
        return [.. Enumerable.Range(0, Rows.Count)
            .Where(index => Rows[index].Bottom < Bounds.Bottom || Rows[index].Height >= tallest - 1)];
    }

    /// <summary>
    /// Reads the geometry of a <c>DataGrid</c> or of a <c>ListView</c> with a <c>GridView</c> through UI Automation:
    /// the column edges from the header items and the rows from the data items.
    /// </summary>
    /// <remarks>
    /// WPF reports the bounds of an element cut to the visible part of its scrolling parents and marks an element
    /// outside it as off screen, so a row cut by the edge of the table has the height of its visible part.
    /// </remarks>
    /// <param name="table">The table element.</param>
    /// <param name="origin">The screen position of the top left corner of the screenshot.</param>
    /// <returns>The geometry in the coordinates of the screenshot.</returns>
    public static TableGeometry Read(AutomationElement table, Point origin)
    {
        var bounds = ToImage(table.BoundingRectangle, origin);
        var columns = table
            .FindAllDescendants(condition => condition.ByControlType(ControlType.HeaderItem))
            .Where(header => !header.IsOffscreen)
            .Select(header => ToImage(header.BoundingRectangle, origin))
            .Where(header => header.Width > 0)
            .Select(header => (header.Left, header.Right))
            .OrderBy(column => column.Left)
            .ToList();
        var rows = table
            .FindAllDescendants(condition => condition.ByControlType(ControlType.DataItem))
            .Where(row => !row.IsOffscreen)
            .Select(row => Rectangle.Intersect(ToImage(row.BoundingRectangle, origin), bounds))
            .Where(row => row.Height > 0)
            .OrderBy(row => row.Top)
            .ToList();
        return new(bounds, columns, rows);
    }

    /// <summary>
    /// Moves a screen rectangle into the coordinates of a screenshot.
    /// </summary>
    /// <param name="screen">The rectangle on the screen.</param>
    /// <param name="origin">The screen position of the top left corner of the screenshot.</param>
    /// <returns>The rectangle in the screenshot.</returns>
    public static Rectangle ToImage(Rectangle screen, Point origin) =>
        new(screen.X - origin.X, screen.Y - origin.Y, screen.Width, screen.Height);
}
