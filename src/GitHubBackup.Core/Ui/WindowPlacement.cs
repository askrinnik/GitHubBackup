namespace GitHubBackup.Core.Ui;

/// <summary>
/// The position and size of a window in device-independent units, and whether it is maximized.
/// </summary>
/// <param name="Left">The left edge of the window in its normal state.</param>
/// <param name="Top">The top edge of the window in its normal state.</param>
/// <param name="Width">The width of the window in its normal state.</param>
/// <param name="Height">The height of the window in its normal state.</param>
/// <param name="IsMaximized">Whether the window is maximized.</param>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized)
{
    /// <summary>The smallest valid <see cref="Width"/>.</summary>
    public const double MinWidth = 400;

    /// <summary>The largest valid <see cref="Width"/>.</summary>
    public const double MaxWidth = 10000;

    /// <summary>The smallest valid <see cref="Height"/>.</summary>
    public const double MinHeight = 300;

    /// <summary>The largest valid <see cref="Height"/>.</summary>
    public const double MaxHeight = 10000;

    /// <summary>
    /// Returns whether the edges are finite and the size lies within <see cref="MinWidth"/>..<see cref="MaxWidth"/>
    /// and <see cref="MinHeight"/>..<see cref="MaxHeight"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the placement can be applied to a window.</returns>
    public bool IsValid() =>
        double.IsFinite(Left)
        && double.IsFinite(Top)
        && Width is >= MinWidth and <= MaxWidth
        && Height is >= MinHeight and <= MaxHeight;
}
