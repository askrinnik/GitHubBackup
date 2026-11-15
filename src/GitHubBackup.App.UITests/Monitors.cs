using System.Drawing;
using System.Runtime.InteropServices;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Asks Windows about the connected monitors, so that a test does not assume where the monitors of the machine lie.
/// </summary>
internal static partial class Monitors
{
    /// <summary>The flag of <see cref="MonitorFromRect"/> that returns no monitor when the rectangle overlaps none.</summary>
    private const uint _monitorDefaultToNull = 0;

    /// <summary>
    /// Returns whether a rectangle overlaps at least one connected monitor.
    /// </summary>
    /// <param name="bounds">The rectangle in pixels, as UI Automation reports the bounds of a window.</param>
    /// <returns><see langword="true"/> when the rectangle overlaps a monitor.</returns>
    public static bool Overlap(Rectangle bounds) =>
        MonitorFromRect(new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), _monitorDefaultToNull) != 0;

    /// <summary>Returns the monitor that a rectangle overlaps most.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="flags">What to return when the rectangle overlaps no monitor.</param>
    /// <returns>The monitor handle, or zero.</returns>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromRect(in Rect rect, uint flags);

    /// <summary>A rectangle in pixels, laid out as the <c>RECT</c> structure of Windows.</summary>
    /// <param name="Left">The left edge.</param>
    /// <param name="Top">The top edge.</param>
    /// <param name="Right">The right edge, exclusive.</param>
    /// <param name="Bottom">The bottom edge, exclusive.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Rect(int Left, int Top, int Right, int Bottom);
}
