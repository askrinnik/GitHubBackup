namespace GitHubBackup.Core.Ui;

/// <summary>
/// The preferences of the user interface that persist between starts: the theme, the placement of the main window
/// and the heights of its panels.
/// </summary>
/// <param name="Theme">The colour mode.</param>
/// <param name="MainWindow">The placement of the main window, or <see langword="null"/> for the default placement.</param>
/// <param name="LogPanelHeight">The height of the live log panel of the main window.</param>
/// <param name="SummaryPanelHeight">The height of the run summary panel of the main window.</param>
public sealed record UiSettings(
    UiTheme Theme,
    WindowPlacement? MainWindow,
    double LogPanelHeight,
    double SummaryPanelHeight)
{
    /// <summary>The smallest valid <see cref="LogPanelHeight"/>.</summary>
    public const double MinLogPanelHeight = 60;

    /// <summary>The largest valid <see cref="LogPanelHeight"/>.</summary>
    public const double MaxLogPanelHeight = 2000;

    /// <summary>The <see cref="LogPanelHeight"/> used when none is saved or the saved one is invalid.</summary>
    public const double DefaultLogPanelHeight = 160;

    /// <summary>The smallest valid <see cref="SummaryPanelHeight"/>.</summary>
    public const double MinSummaryPanelHeight = 80;

    /// <summary>The largest valid <see cref="SummaryPanelHeight"/>.</summary>
    public const double MaxSummaryPanelHeight = 2000;

    /// <summary>The <see cref="SummaryPanelHeight"/> used when none is saved or the saved one is invalid.</summary>
    public const double DefaultSummaryPanelHeight = 220;

    /// <summary>Gets the preferences used when nothing is saved: the system theme, the default placement and panel heights.</summary>
    public static UiSettings Default { get; } =
        new(UiTheme.System, null, DefaultLogPanelHeight, DefaultSummaryPanelHeight);

    /// <summary>
    /// Returns a copy in which every invalid value is replaced by its default; each value is checked on its own.
    /// </summary>
    /// <returns>The preferences with valid values only.</returns>
    public UiSettings Sanitize() => new(
        Enum.IsDefined(Theme) ? Theme : UiTheme.System,
        MainWindow is not null && MainWindow.IsValid() ? MainWindow : null,
        WithinOrDefault(LogPanelHeight, MinLogPanelHeight, MaxLogPanelHeight, DefaultLogPanelHeight),
        WithinOrDefault(SummaryPanelHeight, MinSummaryPanelHeight, MaxSummaryPanelHeight, DefaultSummaryPanelHeight));

    /// <summary>
    /// Returns <paramref name="value"/> when it lies within <paramref name="min"/>..<paramref name="max"/>, otherwise
    /// <paramref name="fallback"/>; <see cref="double.NaN"/> lies within no range.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="min">The smallest valid value.</param>
    /// <param name="max">The largest valid value.</param>
    /// <param name="fallback">The value used when <paramref name="value"/> is invalid.</param>
    /// <returns>The valid value.</returns>
    private static double WithinOrDefault(double value, double min, double max, double fallback) =>
        value >= min && value <= max ? value : fallback;
}
