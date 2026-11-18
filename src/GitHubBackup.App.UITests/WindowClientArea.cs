using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Asks Windows for the client area of a window: the bounds that UI Automation reports for a window include its title
/// bar and its resize frame, so they do not show whether content reaches the window edge.
/// </summary>
internal static partial class WindowClientArea
{
    /// <summary>
    /// Returns the client area of <paramref name="window"/> in screen pixels.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The bounds of the client area.</returns>
    /// <exception cref="InvalidOperationException">Windows does not report the client area of the window.</exception>
    public static Rectangle Bounds(AutomationElement window)
    {
        var handle = window.Properties.NativeWindowHandle.Value;
        if (!GetClientRect(handle, out var client))
        {
            throw new InvalidOperationException($"Windows does not report the client area of the window {handle}.");
        }

        var origin = new NativePoint(0, 0);
        if (!ClientToScreen(handle, ref origin))
        {
            throw new InvalidOperationException($"Windows does not map the client area of the window {handle} to the screen.");
        }

        return new(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
    }

    /// <summary>Returns the client area of a window relative to its own top left corner.</summary>
    /// <param name="window">The window handle.</param>
    /// <param name="rect">The client area.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out NativeRect rect);

    /// <summary>Converts a point of the client area of a window to screen coordinates.</summary>
    /// <param name="window">The window handle.</param>
    /// <param name="point">The point, replaced by its screen coordinates.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint window, ref NativePoint point);

    /// <summary>A rectangle in pixels, laid out as the <c>RECT</c> structure of Windows.</summary>
    /// <param name="Left">The left edge.</param>
    /// <param name="Top">The top edge.</param>
    /// <param name="Right">The right edge, exclusive.</param>
    /// <param name="Bottom">The bottom edge, exclusive.</param>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);

    /// <summary>A point in pixels, laid out as the <c>POINT</c> structure of Windows.</summary>
    /// <param name="X">The horizontal coordinate.</param>
    /// <param name="Y">The vertical coordinate.</param>
    [StructLayout(LayoutKind.Sequential)]
    private record struct NativePoint(int X, int Y);
}
