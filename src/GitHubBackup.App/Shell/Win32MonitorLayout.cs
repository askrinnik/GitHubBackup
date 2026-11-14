using System.Runtime.InteropServices;
using GitHubBackup.Core.Ui;

namespace GitHubBackup.App.Shell;

/// <summary>
/// Implements <see cref="IMonitorLayout"/> with the monitor functions of Windows.
/// </summary>
/// <remarks>
/// Device-independent units are converted to pixels with the DPI of the system; on monitors with different scaling a
/// window near the edge of a monitor can be judged slightly wrong, which only decides between the saved and the default
/// position.
/// </remarks>
internal sealed partial class Win32MonitorLayout : IMonitorLayout
{
    /// <summary>The flag of <see cref="MonitorFromRect"/> that returns no monitor when the rectangle overlaps none.</summary>
    private const uint _monitorDefaultToNull = 0;

    /// <summary>The DPI at which one device-independent unit is one pixel.</summary>
    private const double _defaultDpi = 96;

    /// <inheritdoc />
    public bool IsOnAnyMonitor(WindowPlacement placement)
    {
        var scale = GetDpiForSystem() / _defaultDpi;
        var bounds = new Rect(
            ToPixels(placement.Left, scale),
            ToPixels(placement.Top, scale),
            ToPixels(placement.Left + placement.Width, scale),
            ToPixels(placement.Top + placement.Height, scale));
        return MonitorFromRect(in bounds, _monitorDefaultToNull) != 0;
    }

    /// <summary>
    /// Converts a coordinate from device-independent units to pixels, limited to the range of a pixel coordinate.
    /// </summary>
    /// <param name="value">The coordinate in device-independent units.</param>
    /// <param name="scale">The pixels per device-independent unit.</param>
    /// <returns>The coordinate in pixels.</returns>
    private static int ToPixels(double value, double scale) =>
        (int)Math.Clamp(Math.Round(value * scale), int.MinValue, int.MaxValue);

    /// <summary>Returns the monitor that a rectangle overlaps most.</summary>
    /// <param name="rect">The rectangle in pixels.</param>
    /// <param name="flags">What to return when the rectangle overlaps no monitor.</param>
    /// <returns>The monitor handle, or zero.</returns>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint MonitorFromRect(in Rect rect, uint flags);

    /// <summary>Returns the DPI of the system.</summary>
    /// <returns>The DPI.</returns>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForSystem();

    /// <summary>A rectangle in pixels, laid out as the <c>RECT</c> structure of Windows.</summary>
    /// <param name="Left">The left edge.</param>
    /// <param name="Top">The top edge.</param>
    /// <param name="Right">The right edge, exclusive.</param>
    /// <param name="Bottom">The bottom edge, exclusive.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Rect(int Left, int Top, int Right, int Bottom);
}
